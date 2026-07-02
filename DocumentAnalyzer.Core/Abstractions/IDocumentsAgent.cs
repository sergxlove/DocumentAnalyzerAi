namespace DocumentAnalyzer.Core.Abstractions
{
    public interface IDocumentsAgent
    {
        Task<string> SendMessageAsync(string message, CancellationToken token);
    }
}