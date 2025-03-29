public struct ServerMessage
{
    public int version { get; set; }
    public string content { get; set; }
    public string chatId { get; set; }
    public string userId { get; set; }
    public string messageId { get; set; }
}
