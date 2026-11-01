// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using RepoG33k.Services;

namespace ReviewG33k.Tests;

/// <summary>Checks the Codex protocol without credentials, model calls, or paid usage.</summary>
/// <remarks>A disposable local process verifies sign-in events, read-only drafting, and cancellation.</remarks>
[TestFixture]
public sealed class WorkspaceCodexClientTests
{
    private string m_root;
    private string m_executable;

    [SetUp]
    public void SetUp()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("The local protocol fixture uses a Unix executable script.");
        m_root = Path.Combine(Path.GetTempPath(), "difflens-codex-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_root);
        m_executable = Path.Combine(m_root, "codex-test");
        File.WriteAllText(m_executable, """
            #!/usr/bin/env python3
            import sys, json
            signed_in = False
            wait_for_cancel = False
            def send(value):
                print(json.dumps(value), flush=True)
            for line in sys.stdin:
                msg = json.loads(line)
                method = msg.get('method')
                p = msg.get('params', {})
                result = {}
                if method == 'initialized':
                    continue
                if method == 'account/read':
                    result = {'account': {'type':'chatgpt', 'email':'test@example.invalid'} if signed_in else None}
                elif method == 'account/login/start':
                    assert p['type'] == 'chatgpt'
                    signed_in = True
                    send({'method':'account/login/completed','params':{'loginId':'login-test','success':True}})
                    result = {'loginId':'login-test','authUrl':'https://chatgpt.com/mock-auth'}
                elif method == 'thread/start':
                    wait_for_cancel = 'WAIT_FOR_CANCEL' in p['cwd']
                    assert 'changes' in p['developerInstructions']
                    assert 'credits sensibly' in p['developerInstructions']
                    assert p['sandbox'] == 'read-only'
                    assert p['approvalPolicy'] == 'never'
                    result = {'thread':{'id':'thread-test'}}
                elif method == 'turn/start':
                    assert p['sandboxPolicy']['type'] == 'readOnly'
                    assert p['approvalPolicy'] == 'never'
                    assert p['outputSchema']['required'] == ['summary','details']
                    assert 'changes' in p['input'][0]['text']
                    assert '<staged-diff>' not in p['input'][0]['text']
                    result = {'turn':{'id':'turn-test','status':'inProgress'}}
                    if not wait_for_cancel:
                        text = json.dumps({'summary':'Improve startup','details':'Load independent services concurrently.'})
                        send({'method':'item/completed','params':{'threadId':'thread-test','item':{'type':'agentMessage','text':text}}})
                        send({'method':'turn/completed','params':{'threadId':'thread-test','turn':{'id':'turn-test','status':'completed'}}})
                elif method == 'turn/interrupt':
                    assert p['turnId'] == 'turn-test'
                    send({'method':'turn/completed','params':{'threadId':'thread-test','turn':{'id':'turn-test','status':'interrupted'}}})
                if 'id' in msg:
                    send({'id':msg['id'],'result':result})
            """ + "\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(m_executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [TearDown]
    public void TearDown()
    {
        if (m_root != null)
            Directory.Delete(m_root, recursive: true);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task SignInAndStructuredDraftFollowReadOnlyProtocol(bool staged)
    {
        using var client = new CodexClient(() => m_executable);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.That(await client.ReadAccountAsync(timeout.Token), Is.False);
        Uri opened = null;
        await client.SignInAsync(uri => opened = uri, timeout.Token);
        Assert.That(opened?.AbsoluteUri, Is.EqualTo("https://chatgpt.com/mock-auth"));
        var draft = await client.DraftAsync(m_root, timeout.Token, staged);
        Assert.That(draft.Summary, Is.EqualTo("Improve startup"));
        Assert.That(draft.Details, Does.Contain("concurrently"));
    }

    [Test]
    public async Task CancellingDraftInterruptsItsTurn()
    {
        using var client = new CodexClient(() => m_executable);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.SignInAsync(_ => { }, timeout.Token);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), () => client.DraftAsync(Path.Combine(m_root, "WAIT_FOR_CANCEL"), cancellation.Token));
        Assert.That(await client.ReadAccountAsync(timeout.Token), Is.True);
    }
}
