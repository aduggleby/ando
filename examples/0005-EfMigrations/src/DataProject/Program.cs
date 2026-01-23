// Minimal program for EF Core tools
using DataProject;
using Microsoft.EntityFrameworkCore;

var context = new AppDbContext();
Console.WriteLine("Database path: app.db");
