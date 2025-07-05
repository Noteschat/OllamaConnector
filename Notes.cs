using System.Text.Json.Serialization;

public struct Note
{
    [JsonPropertyName("id")]
    public string Id { get; set; }
    [JsonPropertyName("chatId")]
    public string ChatId { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("content")]
    public string Content { get; set; }
}

public struct AllNotesResponse
{
    [JsonPropertyName("notes")]
    public List<AllNotesResponseNote> Notes { get; set; }
}

public struct AllNotesResponseNote
{
    [JsonPropertyName("id")]
    public string Id { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; }
}

public struct NewNoteBody
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("content")]
    public string Content { get; set; }
}

public struct ChangeNoteBody
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("content")]
    public string Content { get; set; }
}