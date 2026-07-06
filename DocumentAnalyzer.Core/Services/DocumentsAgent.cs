using DocumentAnalyzer.Core.Abstractions;
using DocumentAnalyzer.Core.Infrastructures;
using DocumentAnalyzer.Core.Models;
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
        private string _currentModelId = "google/gemma-4-31b-it";
        private string _apiKey = string.Empty;
        private readonly string _documentText;
        private readonly SemaphoreSlim _rateLimiter = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, ModelConfig> _availableModels;
        private readonly int _maxRetries = 3;
        private readonly int _baseDelayMs = 2000;

        public DocumentsAgent()
        {
            _availableModels = new Dictionary<string, ModelConfig>
            {
                ["google/gemma-4-31b-it"] = new ModelConfig
                {
                    ModelId = "google/gemma-4-31b-it:free", 
                    Endpoint = new Uri("https://openrouter.ai/api/v1"),
                },
                ["poolside/laguna-m.1"] = new ModelConfig
                {
                    ModelId = "poolside/laguna-m.1:free",
                    Endpoint = new Uri("https://openrouter.ai/api/v1"),
                },
                ["nvidia/nemotron-3-super-120b-a12b"] = new ModelConfig
                {
                    ModelId = "nvidia/nemotron-3-super-120b-a12b:free",
                    Endpoint = new Uri("https://openrouter.ai/api/v1"),
                },
            };

            string content = File.ReadAllText("D:\\documents\\openrouterKey.txt", Encoding.UTF8);
            _apiKey = content.Trim();

            if (string.IsNullOrEmpty(_apiKey))
                throw new InvalidOperationException("API ключ не найден");

            _documentText = Reader.ReadDocx("D:\\telegram\\Telegram Download\\Pravilo_1.docx");

            InitializeKernel();
            InitializeChatHistory();

            _settings = new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                Temperature = 0.2
            };
        }

        private void InitializeKernel()
        {
            if (!_availableModels.TryGetValue(_currentModelId, out var config))
                throw new ArgumentException($"Модель {_currentModelId} не найдена");

            _kernel = Kernel.CreateBuilder()
                .AddOpenAIChatCompletion(
                    modelId: config.ModelId,
                    endpoint: config.Endpoint!,
                    apiKey: _apiKey
                ).Build();

            _chat = _kernel.GetRequiredService<IChatCompletionService>();
        }

        private void InitializeChatHistory()
        {
            _history = new ChatHistory();
            _history.AddSystemMessage("Ты — помощник сотрудников компании. " +
                "Ниже приведён полный текст документа (инструкция/регламент). " +
                "Отвечай на вопросы сотрудников, используя ТОЛЬКО этот документ. " +
                "ПРАВИЛА: " +
                "1. Если ответа нет в документе — скажи: 'В документе нет такой информации'. " +
                "2. ОБЯЗАТЕЛЬНО указывай, из какого раздела/пункта взят ответ. " +
                "3. Если информация в разных местах — собери её и укажи все пункты. " +
                "Отвечай на русском языке. Без символов разметки, только текст. " +
                "Вот текст документа: " +
                $"{_documentText}");
        }

        public async Task<string> SendMessageAsync(string model, string message, CancellationToken token)
        {
            await _rateLimiter.WaitAsync(token);
            try
            {
                if (!string.IsNullOrEmpty(model) && model != _currentModelId)
                {
                    if (!_availableModels.ContainsKey(model))
                        throw new ArgumentException($"Модель {model} не найдена");

                    _currentModelId = model;
                    InitializeKernel();
                    InitializeChatHistory();
                }

                _history.AddUserMessage(message);

                Exception? lastException = null;
                for (int attempt = 0; attempt < _maxRetries; attempt++)
                {
                    try
                    {
                        var result = await _chat.GetChatMessageContentsAsync(_history, _settings, _kernel, token);

                        if (result.Count > 0 && result[^1] != null)
                        {
                            var response = result[^1].Content;
                            if (!string.IsNullOrEmpty(response))
                            {
                                _history.AddAssistantMessage(response);
                                return CleanResponse(response);
                            }
                        }

                        throw new InvalidOperationException("Получен пустой ответ от модели");
                    }
                    catch (HttpOperationException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        lastException = ex;
                        if (attempt < _maxRetries - 1)
                        {
                            int delayMs = _baseDelayMs * (int)Math.Pow(2, attempt);
                            delayMs += new Random().Next(0, 500);
                            await Task.Delay(Math.Min(delayMs, 30000), token);
                        }
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        if (attempt < _maxRetries - 1)
                        {
                            int delayMs = _baseDelayMs * (attempt + 1);
                            await Task.Delay(Math.Min(delayMs, 10000), token);
                        }
                    }
                }

                return await TryAlternativeModel(message, token);
            }
            finally
            {
                _rateLimiter.Release();
            }
        }

        private async Task<string> TryAlternativeModel(string message, CancellationToken token)
        {
            var alternativeModels = _availableModels.Keys
                .Where(m => m != _currentModelId)
                .ToList();

            foreach (var altModel in alternativeModels)
            {
                try
                {
                    var originalModel = _currentModelId;
                    _currentModelId = altModel;
                    InitializeKernel();
                    InitializeChatHistory();

                    _history.AddUserMessage(message);

                    var result = await _chat.GetChatMessageContentsAsync(_history, _settings, _kernel, token);

                    if (result.Count > 0 && result[^1] != null)
                    {
                        var response = result[^1].Content;
                        if (!string.IsNullOrEmpty(response))
                        {
                            _history.AddAssistantMessage(response);
                            return CleanResponse(response);
                        }
                    }

                    _currentModelId = originalModel;
                    InitializeKernel();
                    InitializeChatHistory();
                }
                catch
                {
                    continue;
                }
            }

            throw new Exception("Не удалось получить ответ ни от одной модели после всех попыток");
        }

        private string CleanResponse(string content)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            var sb = new StringBuilder(content.Length);
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if (c == '*' && i + 1 < content.Length && content[i + 1] == '*')
                {
                    i++;
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        public List<string> GetAvailableModels()
        {
            return _availableModels.Keys.ToList();
        }

        public string GetCurrentModel()
        {
            return _currentModelId;
        }
    }
}