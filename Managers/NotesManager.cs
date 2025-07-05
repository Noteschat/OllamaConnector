using System.Net;
using System.Text.Json;
using OllamaConnector;

public class NotesManager
{
    public async Task<List<Note>> GetForChat(string sessionId, string chatId)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.Add("Cookie", "sessionId=" + sessionId);

        var notes = new List<Note>();

        try
        {
            var notesResponse = await http.GetAsync($"http://localhost/api/notes/storage/{chatId}");

            if (notesResponse.IsSuccessStatusCode)
            {
                string output = await notesResponse.Content.ReadAsStringAsync();
                var allNotes = JsonSerializer.Deserialize<AllNotesResponse>(output);

                var tasks = new List<Task>();

                foreach (AllNotesResponseNote allNote in allNotes.Notes)
                {
                    tasks.Add(
                        Task.Run(async () =>
                        {
                            (await GetNote(sessionId, chatId, allNote.Id)).Match(
                                note =>
                                {
                                    notes.Add(note);
                                    return 1;
                                },
                                err =>
                                {
                                    return 0;
                                }
                            );
                        })
                    );
                }

                Task.WaitAll(tasks);
            }
        }
        catch
        {
            Logger.Warn($"Failed to retrieve notes for chat {chatId}!");
        }

        return notes;
    }

    public async Task<Either<Note, NotesError>> GetNote(string sessionId, string chatId, string noteId)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.Add("Cookie", "sessionId=" + sessionId);

        try
        {
            var noteResponse = await http.GetAsync($"http://localhost/api/notes/storage/{chatId}/{noteId}");
            if (noteResponse.IsSuccessStatusCode)
            {
                string output = await noteResponse.Content.ReadAsStringAsync();
                var note = JsonSerializer.Deserialize<Note>(output);
                return new Either<Note, NotesError>(note);
            }
        }
        catch { }

        return new Either<Note, NotesError>(NotesError.FailedToGet);
    }
}

public enum NotesError
{
    FailedToGet
}