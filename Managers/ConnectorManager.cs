namespace OllamaConnector.Managers
{
    public class ConnectorManager
    {
        Dictionary<string, ConnectorTask> connectors;
        NotesManager notes;

        public ConnectorManager(NotesManager notes)
        {
            this.notes = notes;

            connectors = new Dictionary<string, ConnectorTask>();
        }

        public void CreateConnector(OllamaConfig config)
        {
            var newConnector = new Connector(config, notes);
            Logger.Info("Got Config: " + config.ConfigId);
            connectors.Add(config.ConfigId, new ConnectorTask { connector = newConnector, task = Task.Run(newConnector.Run) });
        }

        public void StopConnector(string id)
        {
            connectors[id].connector.Stop();
            connectors[id].task.Wait();
            connectors.Remove(id);
        }

        public int GetConnectorCount()
        {
            return connectors.Count;
        }
    }

    struct ConnectorTask
    {
        public Task task;
        public Connector connector;
    }
}
