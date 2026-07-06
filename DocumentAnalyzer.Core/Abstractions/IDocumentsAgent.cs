namespace DocumentAnalyzer.Core.Abstractions
{
    public interface IDocumentsAgent
    {
        Task<string> SendMessageAsync(string model, string message, CancellationToken token);
    }
}