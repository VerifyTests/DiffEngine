namespace DiffEngineTray.Common
{
    public enum MessageBoxIcon
    {
        Error
    }
    
    public interface IMessageBox
    {
        public bool? Show(string message, string title, MessageBoxIcon icon);
    }
}