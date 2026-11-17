// Reserved for a future authenticated API adapter. Start CashHarbor.client for the application.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/health", () => Results.Text("CashHarbor API adapter inactive"));
app.Run();
