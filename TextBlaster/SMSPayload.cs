public class SMSPayload
{
    public string username { get; set; } = string.Empty;
    public string password { get; set; } = string.Empty;
    /// <summary>
    /// Receiver mobile number
    /// </summary>
    public string msisdn { get; set; } = string.Empty;
    public string content { get; set; } = string.Empty;
    /// <summary>
    /// Credential: Sender Id 
    /// </summary>
    public string shortcode_mask { get; set; } = string.Empty;
    /// <summary>
    /// Transaction Id
    /// </summary>
    public string rcvd_transid { get; set; } = string.Empty;
    public bool is_intl { get; set; } = false;
}
