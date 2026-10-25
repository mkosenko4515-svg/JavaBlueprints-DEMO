// ============================================================================
// ФАЙЛ: Program.cs
// [МЕТОДИЧКА]: Минималистичная регистрация всех сервисов нашего пайплайна:
// 1. JavaBlueprintSyncService — двусторонний мост Java <-> Граф
// 2. StaticAnalysisAndBigOService — детектор дефектов и вычислитель Big O
// 3. MonolithOptimizerService — автооптимизатор O(n²)->O(n) и декомпозитор монолита
// 4. JavaQualityGateService — запуск javac/java и сверка поведения на автотестах
// ============================================================================

using JavaBlueprints.Components;
using JavaBlueprints.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Регистрация сервисов ядра Java Blueprints
builder.Services.AddScoped<JavaBlueprintSyncService>();
builder.Services.AddScoped<StaticAnalysisAndBigOService>();
builder.Services.AddScoped<MonolithOptimizerService>();
builder.Services.AddScoped<JavaQualityGateService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();