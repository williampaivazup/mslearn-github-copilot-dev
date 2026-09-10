using ECommerce.ApplicationCore.Entities;
using ECommerce.ApplicationCore.Exceptions;
using ECommerce.ApplicationCore.Interfaces;

namespace ECommerce.ApplicationCore.Services;

/// <summary>
/// Main order processing service
/// </summary>
public class OrderProcessor
{
    private readonly IInventoryService _inventoryService;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IShippingService _shippingService;
    private readonly INotificationService _notificationService;
    private readonly ISecurityValidator _securityValidator;
    private readonly IAuditLogger _auditLogger;

    public OrderProcessor(
        IInventoryService inventoryService,
        IPaymentGateway paymentGateway,
        IShippingService shippingService,
        INotificationService notificationService,
        ISecurityValidator securityValidator,
        IAuditLogger auditLogger)
    {
        _inventoryService = inventoryService;
        _paymentGateway = paymentGateway;
        _shippingService = shippingService;
        _notificationService = notificationService;
        _securityValidator = securityValidator;
        _auditLogger = auditLogger;
    }

    /// <summary>
    /// Responsibilities include:
    /// 1. Input Validation & Security Checks
    /// 2. Inventory Management
    /// 3. Payment Processing
    /// 4. Shipping Management
    /// 5. Customer Notifications
    /// 6. Order Finalization
    /// 7. Audit Logging
    /// 8. Error Handling & Cleanup
    /// </summary>
    public OrderResult ProcessOrder(Order order)
    {
        try
        {
            var validationResult = ValidateOrderAndSecurity(order);
            if (validationResult is not null)
            {
                return validationResult;
            }

            _auditLogger.LogOrderProcessingStarted(order.Id, order.CustomerEmail);

            DisplayOrderSummary(order);

            var inventoryResult = ReserveInventory(order);
            if (inventoryResult is not null)
            {
                return inventoryResult;
            }

            var paymentResult = ProcessPayment(order);
            if (paymentResult is not null)
            {
                return paymentResult;
            }

            var shippingResult = ScheduleShipping(order);
            if (shippingResult is not null)
            {
                return shippingResult;
            }

            SendNotifications(order);
            return CompleteOrder(order);
        }
        catch (Exception ex)
        {
            return HandleUnexpectedError(order, ex);
        }
    }

    private OrderResult? ValidateOrderAndSecurity(Order? order)
    {
        if (order is null)
        {
            _auditLogger.LogSecurityEvent("NULL_ORDER_ATTEMPT", "Attempted to process null order");
            return OrderResult.Failure("No order provided");
        }

        if (order.Items is null || order.Items.Count == 0)
        {
            _auditLogger.LogValidationFailure(order.Id, "Empty order items");
            return OrderResult.Failure("Order has no items");
        }

        if (!_securityValidator.IsValidEmail(order.CustomerEmail))
        {
            _auditLogger.LogValidationFailure(order.Id, "Invalid email format");
            return OrderResult.Failure("Invalid email address format");
        }

        if (!_securityValidator.IsValidShippingAddress(order.ShippingAddress))
        {
            _auditLogger.LogValidationFailure(order.Id, "Invalid shipping address");
            return OrderResult.Failure("Invalid shipping address format");
        }

        if (order.PaymentInfo is null || !_securityValidator.IsValidPaymentInfo(order.PaymentInfo))
        {
            _auditLogger.LogValidationFailure(order.Id, "Invalid payment information");
            return OrderResult.Failure("Payment information is invalid or incomplete");
        }

        var riskScore = _securityValidator.CalculateRiskScore(order);
        if (riskScore > 75)
        {
            _auditLogger.LogSecurityEvent("HIGH_RISK_ORDER", $"Order {order.Id} flagged with risk score: {riskScore}");
            return OrderResult.Failure("Order flagged for manual review due to security concerns");
        }

        if (!_securityValidator.ValidateOrderAmounts(order))
        {
            _auditLogger.LogValidationFailure(order.Id, "Invalid order amounts or pricing");
            return OrderResult.Failure("Order amounts validation failed");
        }

        foreach (var item in order.Items)
        {
            if (!_securityValidator.ValidateOrderItem(item))
            {
                _auditLogger.LogSecurityEvent("INVALID_ITEM_DATA", $"Invalid item data for {item.ProductId}");
                return OrderResult.Failure($"Invalid item data for {item.ProductId}");
            }
        }

        return null;
    }

    private void DisplayOrderSummary(Order order)
    {
        Console.WriteLine($"Processing Order {order.Id} for {_securityValidator.MaskEmail(order.CustomerEmail)}...");
        Console.WriteLine($"Order contains {order.Items.Count} items, Total: ${order.TotalAmount:F2}");
    }

    private OrderResult? ReserveInventory(Order order)
    {
        Console.WriteLine("Checking inventory availability...");

        foreach (var item in order.Items)
        {
            if (!_inventoryService.CheckStock(item.ProductId, item.Quantity))
            {
                _auditLogger.LogInventoryIssue(order.Id, item.ProductId, "Out of stock");
                Console.WriteLine($"Item {item.ProductId} is out of stock. Aborting order.");
                return OrderResult.Failure($"Item {item.ProductId} is out of stock");
            }
        }

        if (!_inventoryService.ReserveStock(order.Items))
        {
            _auditLogger.LogInventoryIssue(order.Id, "ALL_ITEMS", "Failed to reserve inventory");
            Console.WriteLine("Failed to reserve inventory for the order.");
            return OrderResult.Failure("Inventory reservation failed");
        }

        Console.WriteLine("Inventory reserved successfully.");
        _auditLogger.LogInventoryReserved(order.Id, order.Items.Count);
        return null;
    }

    private OrderResult? ProcessPayment(Order order)
    {
        Console.WriteLine("Processing payment...");

        try
        {
            if (!_securityValidator.ValidatePaymentSecurity(order.PaymentInfo, order.TotalAmount))
            {
                _inventoryService.ReleaseStock(order.Items);
                _auditLogger.LogSecurityEvent("PAYMENT_FRAUD_DETECTED", $"Suspicious payment attempt for order {order.Id}");
                return OrderResult.Failure("Payment failed security validation");
            }

            var paymentReference = _paymentGateway.Charge(order.PaymentInfo, order.TotalAmount);
            order.PaymentReference = paymentReference;
            Console.WriteLine($"Payment processed successfully. Reference: {paymentReference}");
            _auditLogger.LogPaymentProcessed(order.Id, order.TotalAmount, paymentReference);
            return null;
        }
        catch (PaymentException ex)
        {
            _inventoryService.ReleaseStock(order.Items);
            _auditLogger.LogPaymentFailure(order.Id, ex.Message);
            Console.WriteLine($"Payment failed for Order {order.Id}: {ex.Message}");
            return OrderResult.Failure("Payment processing failed: " + ex.Message);
        }
    }

    private OrderResult? ScheduleShipping(Order order)
    {
        Console.WriteLine("Scheduling shipping...");

        try
        {
            if (!_securityValidator.ValidateShippingRequirements(order))
            {
                _auditLogger.LogSecurityEvent("SHIPPING_VALIDATION_FAILED", $"Shipping validation failed for order {order.Id}");
                return OrderResult.Failure("Shipping requirements validation failed");
            }

            var shippingDetails = _shippingService.ScheduleShipment(order);
            order.TrackingNumber = shippingDetails.TrackingNumber;
            order.EstimatedDeliveryDate = shippingDetails.EstimatedDelivery;
            Console.WriteLine($"Shipping scheduled successfully. Tracking: {shippingDetails.TrackingNumber}");
            _auditLogger.LogShippingScheduled(order.Id, shippingDetails.TrackingNumber);
            return null;
        }
        catch (Exception ex)
        {
            _auditLogger.LogShippingFailure(order.Id, ex.Message);
            Console.WriteLine($"Error scheduling shipment: {ex.Message}");
            return OrderResult.Failure("Shipping scheduling failed: " + ex.Message);
        }
    }

    private void SendNotifications(Order order)
    {
        Console.WriteLine("Sending notifications...");

        try
        {
            _notificationService.SendOrderConfirmation(order.CustomerEmail, order.Id, order.TrackingNumber);
            Console.WriteLine($"Confirmation sent to {_securityValidator.MaskEmail(order.CustomerEmail)}.");
            _auditLogger.LogNotificationSent(order.Id, "ORDER_CONFIRMATION");

            if (order.TotalAmount > 1000)
            {
                _notificationService.SendHighValueOrderAlert(order);
                _auditLogger.LogNotificationSent(order.Id, "HIGH_VALUE_ALERT");
            }
        }
        catch (Exception ex)
        {
            _auditLogger.LogNotificationFailure(order.Id, ex.Message);
            Console.WriteLine($"Warning: failed to send notification: {ex.Message}");
        }
    }

    private OrderResult CompleteOrder(Order order)
    {
        Console.WriteLine("Finalizing order...");
        order.Status = OrderStatus.Completed;
        order.CompletionDate = DateTime.UtcNow;
        order.ProcessingDuration = DateTime.UtcNow - order.OrderDate;
        Console.WriteLine($"Order {order.Id} completed successfully in {order.ProcessingDuration.TotalSeconds:F1} seconds.");
        _auditLogger.LogOrderCompleted(order.Id, order.TotalAmount);
        return OrderResult.Success(order.Id, order.TrackingNumber ?? "");
    }

    private OrderResult HandleUnexpectedError(Order? order, Exception exception)
    {
        _auditLogger.LogUnexpectedError(order?.Id ?? "UNKNOWN", exception.Message);
        Console.WriteLine($"Unexpected error processing order: {exception.Message}");

        if (order?.Id != null)
        {
            try
            {
                _inventoryService.ReleaseStock(order.Items);
            }
            catch (Exception cleanupException)
            {
                _auditLogger.LogUnexpectedError(order.Id, $"Cleanup failed: {cleanupException.Message}");
            }
        }

        return OrderResult.Failure("An unexpected error occurred during order processing");
    }
}
