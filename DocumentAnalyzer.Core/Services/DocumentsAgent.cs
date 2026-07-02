using DocumentAnalyzer.Core.Abstractions;
using DocumentAnalyzer.Core.Infrastructures;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text;

namespace DocumentAnalyzer.Core.Services
{
    public class DocumentsAgent : IDocumentsAgent
    {
        private Kernel _kernel;
        private ChatHistory _history;
        private OpenAIPromptExecutionSettings _settings;
        private IChatCompletionService _chat;
        public DocumentsAgent()
        {
            string content = File.ReadAllText("D:\\documents\\openrouterKey.txt", Encoding.UTF8);
            string apiKeyTxt = content.Trim();
            _kernel = Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(
                    modelId: "poolside/laguna-m.1:free",
                    endpoint: new Uri("https://openrouter.ai/api/v1"),
                    apiKey: apiKeyTxt
                ).Build();
            _settings = new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                Temperature = 0.2
            };
            _chat = _kernel.GetRequiredService<IChatCompletionService>();
            _history = new ChatHistory();
            string textDocuments = Reader.ReadDocx("D:\\telegram\\Telegram Download\\Pravilo_1.docx");
            _history.AddSystemMessage("Ты — помощник сотрудников компании. " +
                "Ниже приведён полный текст документа (инструкция/регламент). " +
                "Отвечай на вопросы сотрудников, используя ТОЛЬКО этот документ. " +
                "ПРАВИЛА: " +
                "1. Если ответа нет в документе — скажи: 'В документе нет такой информации'. " +
                "2. ОБЯЗАТЕЛЬНО указывай, из какого раздела/пункта взят ответ. " +
                "3. Если информация в разных местах — собери её и укажи все пункты. " +
                "Отвечай на русском языке. " +
                "Вот текст документа: " +
                $"{textDocuments}");
        }

        public async Task<string?> SendMessageAsync(string message, CancellationToken token)
        {
            _history.AddUserMessage(message);
            var result = await _chat.GetChatMessageContentsAsync(_history, _settings, _kernel);
            return result[^1].Content;
        }
    }
}
