import { TestBed } from '@angular/core/testing';
import { ApprovalPanelComponent } from './approval-panel.component';

describe('ApprovalPanelComponent', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders pending approvals', async () => {
    mockFetch([ok({ approvals: [approval()] })]);

    const fixture = await createFixture();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('sanghunmok-prog/nexus-ask-act-hub');
    expect(element.textContent).toContain('Delayed shipments review');
    expect(element.textContent).toContain('nexus-demo');
    expect(element.textContent).toContain('No external action has been executed.');
  });

  it('approves and refreshes the pending list', async () => {
    const fetchMock = mockFetch([
      ok({ approvals: [approval()] }),
      ok({
        approvalId: approval().approvalId,
        status: 'Approved',
        checkpointStatus: 'ReadyToResume',
        resumeAvailable: false,
        message: 'Approval recorded. The checkpoint is marked ready for future resume. No external action has been executed.'
      }),
      ok({ approvals: [] })
    ]);

    const fixture = await createFixture();
    clickButton(fixture.nativeElement, 'Approve');
    await flushPromises();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(element.textContent).toContain('Approved. Checkpoint status: ReadyToResume.');
    expect(element.textContent).toContain('No pending approvals.');
  });

  it('rejects and refreshes the pending list', async () => {
    const fetchMock = mockFetch([
      ok({ approvals: [approval()] }),
      ok({
        approvalId: approval().approvalId,
        status: 'Rejected',
        checkpointStatus: 'Failed',
        resumeAvailable: false,
        message: 'Approval rejected. No external action was executed.'
      }),
      ok({ approvals: [] })
    ]);

    const fixture = await createFixture();
    clickButton(fixture.nativeElement, 'Reject');
    await flushPromises();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(element.textContent).toContain('Rejected. Checkpoint status: Failed.');
    expect(element.textContent).toContain('No pending approvals.');
  });

  it('shows empty state', async () => {
    mockFetch([ok({ approvals: [] })]);

    const fixture = await createFixture();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No pending approvals.');
  });

  it('shows sanitized load errors', async () => {
    mockFetch([
      new Response(
        JSON.stringify({
          code: 'APPROVAL_PERSISTENCE_FAILED',
          message: 'Approval requests could not be loaded.'
        }),
        {
          status: 500,
          headers: { 'Content-Type': 'application/json' }
        }
      )
    ]);

    const fixture = await createFixture();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('APPROVAL_PERSISTENCE_FAILED: Approval requests could not be loaded.');
    expect(text).not.toContain('stack');
  });
});

async function createFixture() {
  await TestBed.configureTestingModule({
    imports: [ApprovalPanelComponent]
  }).compileComponents();

  const fixture = TestBed.createComponent(ApprovalPanelComponent);
  fixture.detectChanges();
  await flushPromises();
  fixture.detectChanges();
  return fixture;
}

async function flushPromises(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0));
  await new Promise((resolve) => setTimeout(resolve, 0));
}

function mockFetch(responses: Response[]) {
  const fetchMock = vi.fn();
  for (const response of responses) {
    fetchMock.mockResolvedValueOnce(response);
  }

  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

function ok(body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' }
  });
}

function approval() {
  return {
    approvalId: '00000000-0000-0000-0000-000000000000',
    correlationId: '11111111-1111-1111-1111-111111111111',
    requestedAtUtc: '2026-04-02T03:00:00Z',
    requestedByUserId: 'demo-user',
    status: 'Pending',
    toolName: 'github.create_issue',
    paramsHash: 'sha256-hex',
    params: {
      repo: 'sanghunmok-prog/nexus-ask-act-hub',
      title: 'Delayed shipments review',
      labels: ['nexus-demo']
    },
    riskSummary: 'Creates a GitHub issue. No action will run until approved.'
  };
}

function clickButton(root: HTMLElement, label: string): void {
  const button = Array.from(root.querySelectorAll('button')).find((candidate) =>
    candidate.textContent?.includes(label)
  );

  if (!button) {
    throw new Error(`Button ${label} was not found.`);
  }

  button.click();
}
