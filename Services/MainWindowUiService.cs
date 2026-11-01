// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System;
using ReviewG33k.ViewModels;

namespace ReviewG33k.Services;

/// <summary>
/// Adapts main-window UI updates behind a small, testable API.
/// </summary>
/// <remarks>
/// Useful for keeping the window code-behind focused on workflow orchestration while this service
/// handles thread-marshaled status/log/progress updates and message dialogs.
/// </remarks>
internal sealed class MainWindowUiService
{
    private readonly MainWindowViewModel m_viewModel;
    private readonly MainWindowLogFeedService m_logFeedService;
    private readonly Func<bool> m_hasUiAccess;
    private readonly Action<Action> m_postToUiThread;
    private readonly Action<LogLineEntry> m_scrollLogEntryIntoView;
    private readonly Action<string, string> m_showMessage;

    public MainWindowUiService(
        MainWindowViewModel viewModel,
        MainWindowLogFeedService logFeedService,
        Func<bool> hasUiAccess,
        Action<Action> postToUiThread,
        Action<LogLineEntry> scrollLogEntryIntoView,
        Action<string, string> showMessage)
    {
        m_viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        m_logFeedService = logFeedService ?? throw new ArgumentNullException(nameof(logFeedService));
        m_hasUiAccess = hasUiAccess ?? throw new ArgumentNullException(nameof(hasUiAccess));
        m_postToUiThread = postToUiThread ?? throw new ArgumentNullException(nameof(postToUiThread));
        m_scrollLogEntryIntoView = scrollLogEntryIntoView ?? throw new ArgumentNullException(nameof(scrollLogEntryIntoView));
        m_showMessage = showMessage ?? throw new ArgumentNullException(nameof(showMessage));
    }

    public void SetStatus(string status)
    {
        ExecuteOnUiThread(() => m_viewModel.StatusText = status);
    }

    public void AppendLog(string message)
    {
        ExecuteOnUiThread(() =>
            m_logFeedService.Append(message, entry => m_scrollLogEntryIntoView(entry)));
    }

    public void ShowMessage(string title, string message)
    {
        ExecuteOnUiThread(() => m_showMessage(title, message));
    }

    public void SetBusyProgressIndeterminate()
    {
        ExecuteOnUiThread(() =>
        {
            if (!m_viewModel.IsBusy)
                return;

            m_viewModel.SetBusyProgressIndeterminate();
        });
    }

    public void UpdateBusyProgress(int completed, int total)
    {
        ExecuteOnUiThread(() =>
        {
            if (!m_viewModel.IsBusy || total <= 0)
            {
                m_viewModel.SetBusyProgressIndeterminate();
                return;
            }

            m_viewModel.UpdateBusyProgress(completed, total);
        });
    }

    private void ExecuteOnUiThread(Action action)
    {
        if (action == null)
            return;

        if (m_hasUiAccess())
        {
            action();
            return;
        }

        m_postToUiThread(action);
    }
}
