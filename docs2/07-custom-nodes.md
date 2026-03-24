# Custom Nodes

When the built-in nodes don't cover your use case, create your own. Custom nodes can do anything: call external SDKs, generate PDFs, interact with message queues, or run complex business logic.

---

## When to Create a Custom Node

- You need to call a third-party SDK (Stripe, Twilio, AWS, etc.)
- Your business logic is too complex for a TransformNode
- You want to generate files (PDF, Excel, CSV)
- You need to interact with services not covered by built-in nodes

---

## Creating a Custom Node

Every custom node implements the `IFlowNode` interface:

```csharp
public interface IFlowNode
{
    string Name { get; }
    string NodeType { get; }
    Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken cancellationToken);
}
```

### Step 1 — Create the Class

```csharp
public class InvoicePdfNode : IFlowNode
{
    public string Name { get; init; }
    public string NodeType => "InvoicePdf";

    public long InvoiceId { get; set; }

    public InvoicePdfNode(string name) => Name = name;

    public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
    {
        if (InvoiceId <= 0)
            return NodeResult.Fail("InvoiceId is required");

        // Your logic here
        var pdfUrl = $"https://cdn.example.com/invoices/{InvoiceId}.pdf";
        context.Set("invoice_pdf_url", pdfUrl);

        return NodeResult.Ok(pdfUrl);
    }
}
```

### Step 2 — Use It in a Flow

```csharp
var serviceId = await FlowBuilder
    .Create("generate_invoice")
    .AddNode(new InvoicePdfNode("create_pdf") { InvoiceId = 42 })
    .AddNode(new EmailSendNode("email_invoice")
    {
        CredentialName = "smtp_primary",
        ToEmail = "user@example.com",
        Subject = "Your Invoice",
        HtmlBodyResolver = ctx =>
            $"<p>Download your invoice: {ctx.Get<string>("invoice_pdf_url")}</p>"
    })
    .StartAsync();
```

### Step 3 — Register for JSON Definitions (Optional)

If you want to use the node in JSON flow definitions:

```csharp
builder.Services.AddBikiranEngine(options =>
{
    options.RegisterNode<InvoicePdfNode>("InvoicePdf");
});
```

Then use in JSON:

```json
{
  "type": "InvoicePdf",
  "name": "create_pdf",
  "params": {
    "invoiceId": "{{invoiceId}}"
  }
}
```

---

## Accessing Services

### Dependency Injection

Use `context.Services` to resolve services from the flow-scoped DI container:

```csharp
public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
{
    var pdfService = context.Services?.GetService<IPdfService>();
    if (pdfService == null)
        return NodeResult.Fail("IPdfService not registered in DI");

    var result = await pdfService.GenerateAsync(InvoiceId, ct);
    context.Set("pdf_result", result);

    return NodeResult.Ok(result);
}
```

### Database Access

For background flows, always use `GetDbContext<T>()` instead of `context.DbContext`:

```csharp
public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
{
    var db = context.GetDbContext<AppDbContext>();
    if (db == null)
        return NodeResult.Fail("AppDbContext not available");

    var order = await db.Orders.FindAsync(new object[] { OrderId }, ct);
    if (order == null)
        return NodeResult.Fail($"Order {OrderId} not found");

    context.Set("order", order);
    return NodeResult.Ok(order);
}
```

### Credentials

Access named credentials registered at startup:

```csharp
public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
{
    var cred = context.GetCredential<GenericCredential>("payment_api");
    var apiKey = cred.Values["ApiKey"];
    var baseUrl = cred.Values["BaseUrl"];

    // Use apiKey and baseUrl for API calls
    return NodeResult.Ok();
}
```

---

## Naming Conventions

| Item          | Convention           | Example                  |
| ------------- | -------------------- | ------------------------ |
| Class name    | PascalCase + "Node"  | `InvoicePdfNode`         |
| NodeType      | PascalCase           | `"InvoicePdf"`           |
| Instance name | lowercase_underscore | `"generate_invoice_pdf"` |

---

## Returning Results

| Method                       | When to Use                             |
| ---------------------------- | --------------------------------------- |
| `NodeResult.Ok()`            | Node completed successfully (no output) |
| `NodeResult.Ok(output)`      | Node completed with an output value     |
| `NodeResult.Fail("message")` | Node failed with a specific error       |

The output value and error message are stored in the node log in the database.

---

## Error Handling

- If your node throws an unhandled exception, the engine catches it and records it as a failure
- Prefer `NodeResult.Fail()` over throwing exceptions for expected errors
- Always pass the `CancellationToken` to async operations so the engine can enforce timeouts

```csharp
public async Task<NodeResult> ExecuteAsync(FlowContext context, CancellationToken ct)
{
    try
    {
        var result = await _httpClient.GetAsync(url, ct);
        return NodeResult.Ok(await result.Content.ReadAsStringAsync(ct));
    }
    catch (HttpRequestException ex)
    {
        return NodeResult.Fail($"HTTP error: {ex.Message}");
    }
}
```

---

## Reading and Writing Context

Nodes share data through `FlowContext`:

```csharp
// Read a value set by a previous node
var userId = context.Get<long>("user_id");

// Write a value for the next node
context.Set("invoice_url", "https://cdn.example.com/invoice.pdf");

// Check if a key exists
if (context.Has("payment_verified"))
{
    // proceed
}
```

---

## Thread Safety

If your node runs inside a `ParallelNode`, the same `FlowContext` is shared across branches. The context dictionary is not thread-safe:

- Write to unique keys per branch (e.g., `"branch1_result"`, `"branch2_result"`)
- Do not read keys that another branch is writing to at the same time
- Do not use shared mutable state

---

## Testing

### Unit Test

```csharp
[Fact]
public async Task InvoicePdfNode_ReturnsSuccess()
{
    var context = new FlowContext { ServiceId = "test-1", FlowName = "test" };
    var node = new InvoicePdfNode("test_pdf") { InvoiceId = 42 };

    var result = await node.ExecuteAsync(context, CancellationToken.None);

    Assert.True(result.Success);
    Assert.True(context.Has("invoice_pdf_url"));
}

[Fact]
public async Task InvoicePdfNode_FailsWithoutInvoiceId()
{
    var context = new FlowContext { ServiceId = "test-2", FlowName = "test" };
    var node = new InvoicePdfNode("test_pdf") { InvoiceId = 0 };

    var result = await node.ExecuteAsync(context, CancellationToken.None);

    Assert.False(result.Success);
    Assert.Equal("InvoiceId is required", result.ErrorMessage);
}
```

### Integration Test

```csharp
[Fact]
public async Task InvoicePdfNode_WorksInFlow()
{
    var serviceId = await FlowBuilder
        .Create("test_pdf_flow")
        .AddNode(new InvoicePdfNode("create_pdf") { InvoiceId = 42 })
        .StartAndWaitAsync();

    Assert.False(string.IsNullOrEmpty(serviceId));
}
```

---

## Checklist

Before using a custom node in production:

- [ ] Implements `IFlowNode` correctly (Name, NodeType, ExecuteAsync)
- [ ] Uses `CancellationToken` for all async operations
- [ ] Returns `NodeResult.Fail()` instead of throwing for expected errors
- [ ] Uses `GetDbContext<T>()` for database access in background flows
- [ ] Writes to unique context keys when used in ParallelNode
- [ ] Has unit tests for success and failure cases
- [ ] Registered via `options.RegisterNode<T>()` if used in JSON definitions
