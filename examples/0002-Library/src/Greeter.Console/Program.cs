using Greeter.Lib;

var service = new GreetingService();
var greeting = service.GetGreeting("World");
Console.WriteLine(greeting);
