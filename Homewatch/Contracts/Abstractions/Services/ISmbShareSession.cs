namespace Homewatch.Contracts.Abstractions.Services
{
    public interface ISmbShareSession : IDisposable
    {
        bool IsConnected { get; }

        void CreateDirectory(string relativePath);

        bool TryUploadFile(Stream source, string relativePath);

        void RenameFile(string sourceRelativePath, string destinationRelativePath);

        byte[]? ReadAllBytes(string relativePath);

        List<string> GetDirectoryNames(string relativePath);

        List<string> GetFileNames(string relativePath, string searchPattern);

        void DeleteFile(string relativePath);

        void DeleteDirectory(string relativePath);
    }
}
