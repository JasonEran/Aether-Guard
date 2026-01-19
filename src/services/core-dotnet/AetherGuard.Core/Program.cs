using AetherGuard.Core.Controllers; // 引用控制器命名空间

var builder = WebApplication.CreateBuilder(args);

// --- 1. 服务注册层 (Dependency Injection) ---
// 注册控制器服务，这是工业级 API 的标准入口
builder.Services.AddControllers(); 
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// --- 2. 中间件管道 (Middleware Pipeline) ---
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// 核心改变：不再使用 MapGet(...) 写死路由
// 而是自动映射 Controller 类中的路由
app.MapControllers(); 

app.Run();