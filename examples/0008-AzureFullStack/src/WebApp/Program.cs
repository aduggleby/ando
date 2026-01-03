// Sample ASP.NET Core Web Application
// Demonstrates integration with Azure SQL Database via Entity Framework Core

using Microsoft.EntityFrameworkCore;
using WebApp.Data;

var builder = WebApplication.CreateBuilder(args);

// Add Entity Framework with SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

// Simple health check endpoint
app.MapGet("/", () => "ANDO Full Stack Example - Running!");

// API endpoint to list todos
app.MapGet("/api/todos", async (AppDbContext db) =>
    await db.Todos.ToListAsync());

// API endpoint to add a todo
app.MapPost("/api/todos", async (TodoItem todo, AppDbContext db) =>
{
    db.Todos.Add(todo);
    await db.SaveChangesAsync();
    return Results.Created($"/api/todos/{todo.Id}", todo);
});

app.Run();
