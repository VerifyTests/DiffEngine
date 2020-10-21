namespace DiffEngineTray.Common
{
    public enum MessageBoxIcon
    {
        Error
    }

    public enum MessageBoxButtons
    {
        YesNo,
        OK
    }

    public enum AlertResult
    {
        NSAlertFirstButtonReturn = 1000,
        NSAlertSecondButtonReturn = 1001,
        NSAlertThirdButtonReturn = 1002
    }

    public interface IMessageBox
    {
        public bool? Show(string message, string title, MessageBoxIcon icon, MessageBoxButtons buttons = MessageBoxButtons.YesNo);
    }
}