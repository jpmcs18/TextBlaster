using DBUtilitiesStandard;
using Microsoft.Extensions.Configuration;
using ObjectExtension;

var builder = new ConfigurationBuilder();
builder.SetBasePath(Directory.GetCurrentDirectory())
       .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

IConfiguration config = builder.Build();

Console.WriteLine("Start...");

Main:
{
    Console.WriteLine("Fetching data...");
    using var db = new DBTools(config["ConnectionString"]);
    //db.ExecuteReaderListStringAsync<>


    Thread.Sleep(1000 * config["MinuteInterval"].ToInt());
}

goto Main;

