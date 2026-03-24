# Custom Nodes

When the built-in nodes don't cover your specific needs, you can create your own. Custom nodes have full access to the shared context, credentials, database, and DI services — just like built-in nodes.

---

## When to Create a Custom Node

| Scenario                                    | Recommendation                     |
| ------------------------------------------- | ---------------------------------- |
| Make an HTTP call                           | Use `HttpRequestNode` (built-in)   |
| Send an email                               | Use `EmailSendNode` (built-in)     |
| Query the database                          | Use `DatabaseQueryNode` (built-in) |
| Generate a PDF, process an image            | Create a custom node               |
| Call a third-party SDK (payment, messaging) | Create a custom node               |
| Complex business logic with multiple steps  | Create a custom node               |
| Reshape or derive data                      | Consider `TransformNode` first     |

---

## The IFlowNode Interface

Every node — built-in or custom — implements this interface:

```csharp
public interface IFlowNode
{
    string Name { get; }        // Unique name within the flow (lowercase_underscore)
    string NodeType { get; }    // Type label (PascalCase)

    Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken);
}
```

---

## Creating a Custom Node

### Step 1 — Write the Class

```csharp
public class InvoicePdfNode : IFlowNode
{
    public string Name { get; }
    public string NodeType => "InvoicePdf";

    public string InvoiceId { get; set; } = "";
    public string OutputKey { get; set; } = "pdf_url";

    public InvoicePdfNode(string name) => Name = name;

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
    {
        // Read input from configuration or from a previous node
        var invoiceId = !string.IsNullOrEmpty(InvoiceId)
            ? InvoiceId
            : context.Get<string>("invoice_id");

        if (string.IsNullOrEmpty(invoiceId))
            return NodeResult.Fail("InvoiceId is required");

        // Use injected services
        var pdfService = context.Services?.GetRequiredService<IPdfService>();
        var pdfUrl = await pdfService!.GenerateAsync(invoiceId, ct);

        // Store the result for downstream nodes
        context.Set(OutputKey, pdfUrl);

        return NodeResult.Ok(pdfUrl);
    }
}
```

### Step 2 — Use It in a Flow

Custom nodes are used exactly like built-in nodes:

```csharp
var serviceId = await FlowBuilder
    .Create("invoice_flow")
    .WithContext(ctx => {
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
            $"<p>Download: <a href=\"{ctx.Get<string>("pdf_url")}\">PDF</a></p>"
    })
    .StartAsync();
```

### Step 3 — Register for JSON Definitions (Optional)

If you want the node available in database-stored flow definitions:

```csharp
builder.Services.AddBikiranEngine(options =>
{
    options.ConnectionString = "...";
    options.RegisterNode<InvoicePdfNode>("InvoicePdf");
});
```

It can then be used in JSON:

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

| Element                  | Convention                    | Example                     |
| ------------------------ | ----------------------------- | --------------------------- |
| Class name               | PascalCase + `Node`           | `InvoicePdfNode`            |
| `NodeType` property      | PascalCase (no `Node` suffix) | `"InvoicePdf"`              |
| `Name` (instance)        | lowercase_underscore          | `"generate_invoice_pdf"`    |
| Configuration properties | PascalCase                    | `InvoiceId`, `OutputKey`    |
| Context keys             | lowercase_underscore          | `"pdf_url"`, `"order_data"` |
| JSON type string         | Same as `NodeType`            | `"InvoicePdf"`              |

---

## Accessing External Services

### Via Dependency Injection

```csharp
var pdfService = context.Services?.GetRequiredService<IPdfService>();
var result = await pdfService!.GenerateAsync(invoiceId, ct);
```

### Via Named Credentials

```csharp
var cred = context.GetCredential<GenericCredential>("payment_gateway");
var apiKey = cred.Values["ApiKey"];
var baseUrl = cred.Values["BaseUrl"];
```

### Via Database

For background flows (`StartAsync()`), resolve the `DbContext` from the flow-scoped DI container to avoid disposed-context errors:

```csharp
var db = context.GetDbContext<AppDbContext>();
if (db == null) return NodeResult.Fail("AppDbContext not registered in DI");

var record = await db.Orders
    .Where(o => o.Id == orderId && o.TimeDeleted == 0)
    .FirstOrDefaultAsync(ct);
```

> **Note:** Do not pass `ctx.DbContext = _context` in `.WithContext()` for background flows — that request-scoped instance will be disposed after the HTTP response is sent. See [Resolving DbContext in Nodes](03-building-flows.md#resolving-dbcontext-in-nodes).

---

## Error Handling Best Practices

### Return Failures Instead of Throwing

Use `NodeResult.Fail(message)` for expected errors. The engine catches unhandled exceptions, but explicit failure results produce better log messages.

```csharp
// Validate inputs
if (string.IsNullOrEmpty(invoiceId))
    return NodeResult.Fail("InvoiceId is required");

// Catch expected errors
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

### Always Pass the CancellationToken

This ensures flows can be cancelled and timeouts are respected:

```csharp
// Correct — passes the token
var result = await httpClient.GetAsync(url, ct);

// Incorrect — ignores cancellation
var result = await httpClient.GetAsync(url);
```

---

## Reading and Writing Context

### Reading Values

```csharp
var orderId = context.Get<string>("order_id");
var amount = context.Get<double>("order_total");

if (context.Has("payment_result"))
{
    var payment = context.Get<PaymentResult>("payment_result");
}
```

### Writing Values

```csharp
context.Set("pdf_url", "https://cdn.example.com/invoices/123.pdf");
context.Set("invoice_data", new { Id = "INV-001", Amount = 150.00 });
```

Use descriptive, unique `lowercase_underscore` keys to avoid collisions between nodes.

---

## Thread Safety in Parallel Branches

When your node runs inside a `ParallelNode`, multiple branches execute at the same time. The shared context dictionary is **not thread-safe**.

**Rules:**

- Each branch must write to its own unique context key
- Do not read a key that another branch is currently writing to
- Do not modify shared mutable objects stored in context

---

## Testing

### Unit Test with Mocked Services

```csharp
[Fact]
public async Task InvoicePdfNode_SetsOutputKey_OnSuccess()
{
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

    var result = await node.ExecuteAsync(context, CancellationToken.None);

    Assert.True(result.Success);
    Assert.Equal("https://cdn.example.com/invoices/test.pdf", context.Get<string>("pdf_url"));
}
```

### Integration Test in a Flow

```csharp
[Fact]
public async Task InvoicePdfNode_WorksInFlow()
{
    var serviceId = await FlowBuilder
        .Create("test_invoice_flow")
        .WithContext(ctx => { ctx.Services = _serviceProvider; })
        .AddNode(new InvoicePdfNode("generate_pdf") {
            InvoiceId = "INV-001",
            OutputKey = "pdf_url"
        })
        .StartAndWaitAsync();

    var run = await _context.FlowRun
        .FirstOrDefaultAsync(r => r.ServiceId == serviceId);

    Assert.Equal("completed", run?.Status);
}
```

---

## Full Example: Payment Verification Node

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
        var txnId = !string.IsNullOrEmpty(TransactionId)
            ? TransactionId
            : context.Get<string>("transaction_id");

        if (string.IsNullOrEmpty(txnId))
            return NodeResult.Fail("TransactionId is required");

        var cred = context.GetCredential<GenericCredential>(CredentialName);
        var apiKey = cred.Values["ApiKey"];
        var baseUrl = cred.Values["BaseUrl"];

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

---

## Pre-Production Checklist

Before using a custom node in production, verify:

- [ ] Implements `IFlowNode` with `Name`, `NodeType`, and `ExecuteAsync`
- [ ] `NodeType` does not conflict with built-in types (`Wait`, `HttpRequest`, `EmailSend`, etc.)
- [ ] Uses `NodeResult.Ok()` / `NodeResult.Fail()` instead of throwing exceptions
- [ ] Passes `CancellationToken` to all async calls
- [ ] Writes to a unique context key (uses `OutputKey` pattern)
- [ ] Validates required inputs before doing work
- [ ] Thread-safe when used in parallel branches
- [ ] Has at least one unit test
- [ ] Registered for JSON definitions if needed
