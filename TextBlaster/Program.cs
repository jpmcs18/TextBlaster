using DBUtilitiesStandard;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using ObjectExtension;
using System.Text;
using System.Text.RegularExpressions;

var builder = new ConfigurationBuilder();
builder.SetBasePath(Directory.GetCurrentDirectory())
       .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

IConfiguration config = builder.Build();
Console.WriteLine("Start...");

string pattern = @"@\|inquiry:\d+\|";
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

                var message = textBlast.Message;
                MatchCollection matches = Regex.Matches(message, pattern);
                if(matches?.Any() ?? false)
                {
                    foreach (Match match in matches)
                    {
                        db.ClearParameter();
                        db.AddInParameter("@Id", match.Value.Substring(match.Value.IndexOf(":") + 1, match.Value.LastIndexOf("|") - (match.Value.IndexOf(":") + 1)));
                        var inquiry = await db.ExecuteReaderStringAsync<Inquiry>("SELECT * FROM [Inquiry] WHERE ID = @Id");
                        message = message.Replace(match.Value, inquiry?.SourceID ?? "");
                    }
                }

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

                        var hasSuccess = false;
                        var hasError = false;
                        if (individual.MobileNumber.Contains(','))
                        {
                            var numbers = individual.MobileNumber.Split(",");
                            foreach (var number in numbers)
                            {
                                try
                                {
                                    hasSuccess = await SendMessage(config, message, number) ? true : hasSuccess;
                                }
                                catch
                                {
                                    hasError = true;
                                }
                            }
                        }
                        else
                        {
                            try
                            {
                                hasSuccess = await SendMessage(config, message, individual.MobileNumber) ? true : hasSuccess;
                            }
                            catch
                            {
                                hasError = true;
                            }
                        }

                        db.ClearParameter();
                        db.AddInParameter("@TextBlastId", textBlast.ID);
                        db.AddInParameter("@MobileNumber", individual.MobileNumber);
                        db.AddInParameter("@StatusId", hasSuccess ? 3 : hasError ? 1 : 5);
                        await db.ExecuteNonQueryStringAsync("UPDATE [TextBlastIndividualSending] " +
                                                            "SET [StatusId] = @StatusId " +
                                                            "   , [ModifiedDate] = GETDATE()" +
                                                            "WHERE [TextBlastId] = @TextBlastId " +
                                                            "   AND [MobileNumber] = @MobileNumber");
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
    finally { Thread.Sleep(1000 * config["SecondInterval"].ToInt()); }
}

goto Main;

static async Task<bool> SendMessage(IConfiguration config, string message, string number)
{
    var sms = new SMSPayload
    {
        username = config["Username"]!,
        password = config["Password"]!,
        msisdn = number,
        content = message,
        shortcode_mask = config["ShortCodeMask"]!,
    };

    try
    {
        StringContent data = new(JsonConvert.SerializeObject(sms), Encoding.UTF8, "application/json");
        using HttpClient hc = new();
        using HttpResponseMessage response = await hc.PostAsync(config["BaseUrl"]!, data);
        switch (response.StatusCode)
        {
            case System.Net.HttpStatusCode.Created: return true;
            case System.Net.HttpStatusCode.BadRequest: return false;
            default: throw new Exception("Unable to send");
        };
    }
    catch(Exception) { throw; }
}