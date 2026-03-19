# Custom Node Creation Guide

This guide explains how to create custom nodes for Bikiran.Engine — from basic implementation to advanced patterns.

---

## When to Create a Custom Node

Use a custom node when the built-in nodes don't cover your specific business logic:

| Scenario                                    | Recommendation                     |
| ------------------------------------------- | ---------------------------------- |
| Make an HTTP call                           | Use `HttpRequestNode` (built-in)   |
| Send an email                               | Use `EmailSendNode` (built-in)     |
| Query the database                          | Use `DatabaseQueryNode` (built-in) |
| Generate a PDF, process an image            | **Create a custom node**           |
| Call a third-party SDK (payment, messaging) | **Create a custom node**           |
| Complex business logic with multiple steps  | **Create a custom node**           |
| Validate or transform data in a custom way  | Consider `TransformNode` first     |

---

## Anatomy of a Custom Node

Every node implements the `IFlowNode` interface:

```csharp
public interface IFlowNode
{
    string Name { get; }        // Unique name within the flow (lowercase_underscore)
    string NodeType { get; }    // Type label (PascalCase, e.g. "InvoicePdf")

    Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken);
}
```

### Required Members

| Member         | Convention             | Example                |
| -------------- | ---------------------- | ---------------------- |
| `Name`         | `lowercase_underscore` | `"generate_invoice"`   |
| `NodeType`     | `PascalCase`           | `"InvoicePdf"`         |
| `ExecuteAsync` | Return `NodeResult`    | `NodeResult.Ok(value)` |

---

## Step-by-Step: Creating a Custom Node

### Step 1 — Create the Class

Create a class that implements `IFlowNode`. Place it alongside your application code or in a shared library.

```csharp
public class InvoicePdfNode : IFlowNode
{
    // Identity
    public string Name { get; }
    public string NodeType => "InvoicePdf";

    // Configuration properties (set by the caller)
    public string InvoiceId { get; set; } = "";
    public string OutputKey { get; set; } = "pdf_url";

    // Constructor — name is required
    public InvoicePdfNode(string name) => Name = name;

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
    {
        // 1. Read input from configuration or context
        var invoiceId = !string.IsNullOrEmpty(InvoiceId)
            ? InvoiceId
            : context.Get<string>("invoice_id");

        if (string.IsNullOrEmpty(invoiceId))
            return NodeResult.Fail("InvoiceId is required");

        // 2. Perform the work
        var pdfService = context.Services?.GetRequiredService<IPdfService>();
        var pdfUrl = await pdfService!.GenerateAsync(invoiceId, ct);

        // 3. Store the result for downstream nodes
        context.Set(OutputKey, pdfUrl);

        // 4. Return success with optional output
        return NodeResult.Ok(pdfUrl);
    }
}
```

### Step 2 — Use in a Flow

Custom nodes are used exactly like built-in nodes:

```csharp
var serviceId = await FlowBuilder
    .Create("invoice_flow")
    .WithContext(ctx => {
        ctx.Services = _serviceProvider;
        ctx.HttpContext = HttpContext;
    })
    .AddNode(new HttpRequestNode("fetch_order") {
        Url = "https://api.example.com/order/123",
        OutputKey = "order_data"
    })
    .AddNode(new InvoicePdfNode("generate_pdf") {
        InvoiceId = "INV-2024-001",
        OutputKey = "pdf_url"
    })
    .AddNode(new EmailSendNode("send_invoice") {
        CredentialName = "smtp_primary",
        ToEmail = "customer@example.com",
        Subject = "Your Invoice",
        HtmlBodyResolver = ctx =>
            $"<p>Download your invoice: <a href=\"{ctx.Get<string>("pdf_url")}\">PDF</a></p>"
    })
    .StartAsync();
```

### Step 3 — Register for JSON Definitions (Optional)

If you want the node to be usable in database-stored flow definitions (JSON), register it at startup:

```csharp
builder.Services.AddBikiranEngine(options =>
{
    options.ConnectionString = "...";
    options.RegisterNode<InvoicePdfNode>("InvoicePdf");
});
```

Once registered, the node type can appear in JSON flow definitions:

```json
{
  "type": "InvoicePdf",
  "name": "generate_pdf",
  "params": {
    "invoiceId": "{{invoiceId}}",
    "outputKey": "pdf_url"
  }
}
```

---

## Naming Conventions

| Element                  | Convention               | Example                     |
| ------------------------ | ------------------------ | --------------------------- |
| Class name               | `PascalCase` + `Node`    | `InvoicePdfNode`            |
| `NodeType` property      | `PascalCase` (no `Node`) | `"InvoicePdf"`              |
| `Name` (instance)        | `lowercase_underscore`   | `"generate_invoice_pdf"`    |
| Configuration properties | `PascalCase`             | `InvoiceId`, `OutputKey`    |
| Context keys             | `lowercase_underscore`   | `"pdf_url"`, `"order_data"` |
| JSON type string         | Same as `NodeType`       | `"InvoicePdf"`              |

---

## Accessing External Services

### Via Dependency Injection

Use `FlowContext.Services` to resolve services from the DI container:

```csharp
public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
{
    var pdfService = context.Services?.GetRequiredService<IPdfService>();
    var result = await pdfService!.GenerateAsync(invoiceId, ct);

    context.Set(OutputKey, result);
    return NodeResult.Ok(result);
}
```

### Via Named Credentials

Use `FlowContext.GetCredential<T>()` for secrets registered at startup:

```csharp
public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
{
    var cred = context.GetCredential<GenericCredential>("payment_gateway");
    var apiKey = cred.Values["ApiKey"];
    var baseUrl = cred.Values["BaseUrl"];

    // Use apiKey and baseUrl to call the external service
    // ...

    return NodeResult.Ok(result);
}
```

### Via Database

Use `FlowContext.DbContext` for EF Core queries:

```csharp
public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
{
    var db = context.DbContext;
    if (db == null) return NodeResult.Fail("DbContext is required");

    var record = await db.Orders
        .Where(o => o.Id == orderId && o.TimeDeleted == 0)
        .FirstOrDefaultAsync(ct);

    context.Set(OutputKey, record);
    return NodeResult.Ok(record);
}
```

---

## Error Handling

### Return Failures — Don't Throw

Nodes should return `NodeResult.Fail(message)` rather than throwing exceptions. The engine catches unhandled exceptions but using `NodeResult` gives better error messages in the logs.

```csharp
// Good — explicit failure
if (string.IsNullOrEmpty(invoiceId))
    return NodeResult.Fail("InvoiceId is required");

// Good — catch expected errors
try
{
    var result = await externalService.CallAsync(ct);
    context.Set(OutputKey, result);
    return NodeResult.Ok(result);
}
catch (HttpRequestException ex)
{
    return NodeResult.Fail($"External service error: {ex.Message}");
}
```

### Respect Cancellation

Always pass the `CancellationToken` to async operations. This ensures flows can be cancelled and timeouts are respected:

```csharp
// Good — passes cancellation token
var result = await httpClient.GetAsync(url, ct);

// Bad — ignores cancellation
var result = await httpClient.GetAsync(url);
```

---

## Reading and Writing Context

### Reading Values from Previous Nodes

```csharp
// Read a typed value
var orderId = context.Get<string>("order_id");
var amount = context.Get<double>("order_total");

// Check if a key exists before reading
if (context.Has("payment_result"))
{
    var payment = context.Get<PaymentResult>("payment_result");
}
```

### Writing Values for Downstream Nodes

```csharp
// Store a simple value
context.Set("pdf_url", "https://cdn.example.com/invoices/123.pdf");

// Store a complex object
context.Set("invoice_data", new { Id = "INV-001", Amount = 150.00 });
```

### Key Naming

Use `lowercase_underscore` keys. Each node should write to its own unique key to avoid collisions:

```csharp
// Good — unique, descriptive keys
context.Set("invoice_pdf_url", pdfUrl);
context.Set("payment_verified", true);

// Bad — generic or colliding keys
context.Set("result", pdfUrl);
context.Set("data", paymentData);
```

---

## Thread Safety in Parallel Branches

When your node runs inside a `ParallelNode`, multiple branches execute concurrently. The shared `FlowContext.Variables` dictionary is **not thread-safe**.

**Rules:**

- Each branch must write to its own unique context key.
- Do not read a key that another concurrent branch is writing to.
- Do not modify shared mutable objects stored in context.

```csharp
// Safe — each branch writes its own key
// Branch 1
context.Set("email_result", emailResult);

// Branch 2
context.Set("webhook_result", webhookResult);
```

---

## Testing Custom Nodes

### Unit Testing with InMemoryFlowLogger

```csharp
[Fact]
public async Task InvoicePdfNode_SetsOutputKey_OnSuccess()
{
    // Arrange
    var mockPdfService = new Mock<IPdfService>();
    mockPdfService
        .Setup(s => s.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync("https://cdn.example.com/invoices/test.pdf");

    var services = new ServiceCollection()
        .AddSingleton(mockPdfService.Object)
        .BuildServiceProvider();

    var context = new FlowContext
    {
        ServiceId = Guid.NewGuid().ToString(),
        FlowName = "test_flow",
        Services = services
    };

    var node = new InvoicePdfNode("generate_pdf")
    {
        InvoiceId = "INV-001",
        OutputKey = "pdf_url"
    };

    // Act
    var result = await node.ExecuteAsync(context, CancellationToken.None);

    // Assert
    Assert.True(result.Success);
    Assert.Equal("https://cdn.example.com/invoices/test.pdf", context.Get<string>("pdf_url"));
}
```

### Integration Testing in a Flow

```csharp
[Fact]
public async Task InvoicePdfNode_WorksInFlow()
{
    var serviceId = await FlowBuilder
        .Create("test_invoice_flow")
        .WithContext(ctx => {
            ctx.Services = _serviceProvider;
        })
        .AddNode(new InvoicePdfNode("generate_pdf") {
            InvoiceId = "INV-001",
            OutputKey = "pdf_url"
        })
        .StartAndWaitAsync();

    // Query the FlowRun and verify status = completed
    var run = await _context.FlowRun
        .FirstOrDefaultAsync(r => r.ServiceId == serviceId);

    Assert.Equal("completed", run?.Status);
}
```

---

## Complete Example: Payment Verification Node

A real-world example that verifies a payment through an external gateway:

```csharp
public class PaymentVerifyNode : IFlowNode
{
    public string Name { get; }
    public string NodeType => "PaymentVerify";

    public string TransactionId { get; set; } = "";
    public string CredentialName { get; set; } = "payment_gateway";
    public string OutputKey { get; set; } = "payment_verified";

    public PaymentVerifyNode(string name) => Name = name;

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
    {
        // Validate input
        var txnId = !string.IsNullOrEmpty(TransactionId)
            ? TransactionId
            : context.Get<string>("transaction_id");

        if (string.IsNullOrEmpty(txnId))
            return NodeResult.Fail("TransactionId is required");

        // Resolve credentials
        var cred = context.GetCredential<GenericCredential>(CredentialName);
        var apiKey = cred.Values["ApiKey"];
        var baseUrl = cred.Values["BaseUrl"];

        // Call external API
        var httpClient = context.Services?.GetRequiredService<IHttpClientFactory>()
            .CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{baseUrl}/verify?txn_id={Uri.EscapeDataString(txnId)}");
        request.Headers.Add("Authorization", $"Bearer {apiKey}");

        try
        {
            var response = await httpClient!.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(ct);

            context.Set(OutputKey, body);
            return NodeResult.Ok(body);
        }
        catch (HttpRequestException ex)
        {
            return NodeResult.Fail($"Payment verification failed: {ex.Message}");
        }
    }
}
```

**Usage:**

```csharp
var serviceId = await FlowBuilder
    .Create("order_payment_verify")
    .WithContext(ctx => {
        ctx.Services = _serviceProvider;
    })
    .AddNode(new PaymentVerifyNode("verify_payment") {
        TransactionId = "TXN-12345",
        CredentialName = "payment_gateway",
        OutputKey = "payment_result"
    })
    .AddNode(new IfElseNode("check_payment") {
        Condition = ctx => ctx.Has("payment_result"),
        TrueBranch = [
            new EmailSendNode("send_receipt") {
                CredentialName = "smtp_primary",
                ToEmail = customerEmail,
                Subject = "Payment Confirmed",
                Template = "PAYMENT_RECEIPT"
            }
        ],
        FalseBranch = [
            new EmailSendNode("send_failure") {
                CredentialName = "smtp_primary",
                ToEmail = adminEmail,
                Subject = "Payment Verification Failed",
                TextBody = "Manual review required."
            }
        ]
    })
    .StartAsync();
```

---

## Checklist

Before using your custom node in production:

- [ ] **Implements `IFlowNode`** — `Name`, `NodeType`, `ExecuteAsync` are all defined
- [ ] **`NodeType` is unique** — does not conflict with built-in types (`Wait`, `HttpRequest`, `EmailSend`, etc.)
- [ ] **Returns `NodeResult`** — uses `NodeResult.Ok()` / `NodeResult.Fail()` instead of throwing
- [ ] **Respects `CancellationToken`** — passes `ct` to all async calls
- [ ] **Writes to a unique context key** — uses `OutputKey` pattern, avoids generic names
- [ ] **Handles missing inputs** — validates required configuration and context values
- [ ] **Thread-safe for parallel use** — writes only to its own unique context key
- [ ] **Unit tested** — at least one test with a mocked `FlowContext`
- [ ] **Registered for JSON** (if needed) — `options.RegisterNode<T>("TypeName")` in startup
