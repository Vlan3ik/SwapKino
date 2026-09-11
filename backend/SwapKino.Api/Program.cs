using SwapKino.Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddSwapKinoServices();

var app = builder.Build();
await app.InitializeSwapKinoDatabase();
app.MapSwapKinoApplication();
await app.RunAsync();

public partial class Program
{
    protected Program() { }
}
