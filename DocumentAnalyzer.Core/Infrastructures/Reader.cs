using DocumentFormat.OpenXml.Packaging;

namespace DocumentAnalyzer.Core.Infrastructures
{
    public static class Reader
    {
        public static string ReadDocx(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Путь к файлу не может быть пустым");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Файл не найден: {path}");
            string extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Файл должен иметь расширение .docx, текущее: {extension}");
            var fileInfo = new FileInfo(path);
            long maxFileSize = 100 * 1024 * 1024; 
            if (fileInfo.Length > maxFileSize)
                throw new InvalidOperationException($"Файл слишком большой. Максимальный размер:" +
                    $" {maxFileSize / 1024 / 1024} MB");
            using var doc = WordprocessingDocument.Open(path, false);
            return doc.MainDocumentPart!.Document!.Body!.InnerText;
        }
    }
}
