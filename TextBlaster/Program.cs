using DBUtilitiesStandard;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using ObjectExtension;
using System.Text;

var builder = new ConfigurationBuilder();
builder.SetBasePath(Directory.GetCurrentDirectory())
       .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

IConfiguration config = builder.Build();
Console.WriteLine("Start...");
Main:
{
    
    Console.WriteLine("Fetching data...");
    try
    {
        using var db = new DBTools(config["ConnectionString"]);
        db.ClearParameter();
        db.AddInParameter("@Date", DateTime.Now.AddSeconds(-30));
        var textBlasts = await db.ExecuteReaderListStringAsync<TextBlast>("SELECT * " +
                                                                        "FROM [TextBlast] " +
                                                                        "WHERE [TextBlastStatusID] = 1 " +
                                                                        "   AND [RunSchedule] <= @Date");
        if (textBlasts?.Any() ?? false)
        {

            Console.WriteLine("Sending Messages...");
            foreach (var textBlast in textBlasts)
            {
                db.ClearParameter();
                db.AddInParameter("@Id", textBlast.ID);
                await db.ExecuteNonQueryStringAsync("UPDATE [TextBlast] " +
                                                    "SET [TextBlastStatusID] = 2 " +
                                                    "   , [ModifiedBy] = -1" +
                                                    "   , [ModifiedDate] = GETDATE()" +
                                                    "WHERE [ID] = @Id");

                db.ClearParameter();
                db.AddInParameter("@TextBlastId", textBlast.ID);
                var individuals = await db.ExecuteReaderListStringAsync<TextBlastIndividualSending>("SELECT * " +
                                                                                                    "FROM [TextBlastIndividualSending] " +
                                                                                                    "WHERE [TextBlastId] = @TextBlastId " +
                                                                                                    "   AND [StatusId] = 1");
                if (individuals?.Any() ?? false)
                {
                    foreach (var individual in individuals)
                    {
                        var sms = new SMSPayload
                        {
                            username = config["Username"]!,
                            password = config["Password"]!,
                            msisdn = individual.MobileNumber,
                            content = textBlast.Message,
                            shortcode_mask = config["ShortCodeMask"]!,
                            rcvd_transid = textBlast.ID.ToString(),
                        };

                        try
                        {
                            StringContent data = new(JsonConvert.SerializeObject(sms), Encoding.UTF8, "application/json");
                            using HttpClient hc = new();
                            using HttpResponseMessage response = await hc.PostAsync(config["BaseUrl"]!, data);
                            switch (response.StatusCode)
                            {
                                case System.Net.HttpStatusCode.Created:
                                    db.ClearParameter();
                                    db.AddInParameter("@TextBlastId", textBlast.ID);
                                    db.AddInParameter("@MobileNumber", individual.MobileNumber);
                                    await db.ExecuteNonQueryStringAsync("UPDATE [TextBlastIndividualSending] " +
                                                                        "SET [StatusId] = 3 " +
                                                                        "   , [ModifiedDate] = GETDATE()" +
                                                                        "WHERE [TextBlastId] = @TextBlastId " +
                                                                        "   AND [MobileNumber] = @MobileNumber");
                                    break;
                                case System.Net.HttpStatusCode.BadRequest:
                                    db.ClearParameter();
                                    db.AddInParameter("@TextBlastId", textBlast.ID);
                                    db.AddInParameter("@MobileNumber", individual.MobileNumber);
                                    await db.ExecuteNonQueryStringAsync("UPDATE [TextBlastIndividualSending] " +
                                                                        "SET [StatusId] = 5 " +
                                                                        "   , [ModifiedDate] = GETDATE()" +
                                                                        "WHERE [TextBlastId] = @TextBlastId " +
                                                                        "   AND [MobileNumber] = @MobileNumber");
                                    break;
                                default:
                                    db.ClearParameter();
                                    db.AddInParameter("@TextBlastId", textBlast.ID);
                                    db.AddInParameter("@MobileNumber", individual.MobileNumber);
                                    await db.ExecuteNonQueryStringAsync("UPDATE [TextBlastIndividualSending] " +
                                                                        "SET [StatusId] = 1 " +
                                                                        "   , [ModifiedDate] = GETDATE()" +
                                                                        "WHERE [TextBlastId] = @TextBlastId " +
                                                                        "   AND [MobileNumber] = @MobileNumber");
                                    break;
                            };
                        }
                        catch { }
                    }
                }

                db.ClearParameter();
                db.AddInParameter("@Id", textBlast.ID);
                //Message All Sent/No Recepient
                await db.ExecuteNonQueryStringAsync("UPDATE [TextBlast] " +
                    "SET [TextBlastStatusID] = IIF((SELECT COUNT([TextBlastId]) " +
                    "       FROM [TextBlastIndividualSending] " +
                    "       WHERE [TextBlastId] = [TextBlast].ID " +
                    "           AND [StatusId] = 1) = 0, 3, 1)" +
                    "   , [ModifiedBy] = -1" +
                    "   , [ModifiedDate] = GETDATE()" +
                    "WHERE [ID] = @Id");
            }
            Console.WriteLine("Closing Messages...");
        }
    }
    catch {  }
    finally { Thread.Sleep(1000 * config["MinuteInterval"].ToInt()); }
}

goto Main;
