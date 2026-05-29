import { TestBed } from '@angular/core/testing';
import { TraceTimelineComponent } from './trace-timeline.component';

describe('TraceTimelineComponent', () => {
  it('renders docs.search result counts and top source without row or citation defaults', async () => {
    const fixture = await createFixture([
      event('tool.result', {
        toolName: 'docs.search',
        resultCount: 4,
        topResult: {
          citationId: 'doc-1',
          sourceName: 'nexus-demo-policy.txt',
          title: 'nexus-demo-policy'
        },
        result: {
          resultCount: 4,
          results: [{ citationId: 'doc-1' }]
        }
      })
    ]);

    const text = pageText(fixture);
    expect(text).toContain('docs.search');
    expect(text).toContain('Results');
    expect(text).toContain('4');
    expect(text).toContain('Top');
    expect(text).toContain('nexus-demo-policy.txt');
    expect(text).not.toContain('Rows');
    expect(text).not.toContain('Citations');
  });

  it('falls back to nested docs.search resultCount', async () => {
    const fixture = await createFixture([
      event('tool.result', {
        toolName: 'docs.search',
        result: {
          resultCount: 3,
          results: [{ citationId: 'doc-1' }, { citationId: 'doc-2' }, { citationId: 'doc-3' }]
        }
      })
    ]);

    const text = pageText(fixture);
    expect(text).toContain('Results');
    expect(text).toContain('3');
    expect(text).not.toContain('Rows');
    expect(text).not.toContain('Citations');
  });

  it('falls back to docs.search results length', async () => {
    const fixture = await createFixture([
      event('tool.result', {
        toolName: 'docs.search',
        result: {
          results: [{ citationId: 'doc-1' }, { citationId: 'doc-2' }]
        }
      })
    ]);

    const text = pageText(fixture);
    expect(text).toContain('Results');
    expect(text).toContain('2');
    expect(text).not.toContain('Rows');
    expect(text).not.toContain('Citations');
  });

  it('renders docs.get_chunk source and chunk information', async () => {
    const fixture = await createFixture([
      event('tool.result', {
        toolName: 'docs.get_chunk',
        citationId: 'doc-1',
        sourceName: 'nexus-demo-policy.txt',
        title: 'nexus-demo-policy',
        chunkIndex: 0,
        chunkTextLength: 1000
      })
    ]);

    const text = pageText(fixture);
    expect(text).toContain('docs.get_chunk');
    expect(text).toContain('Chunk loaded');
    expect(text).toContain('Source');
    expect(text).toContain('nexus-demo-policy.txt');
    expect(text).toContain('Chunk');
    expect(text).toContain('0');
    expect(text).toContain('Citation');
    expect(text).toContain('Available');
    expect(text).not.toContain('Rows');
  });

  it('renders db.query_readonly success rows', async () => {
    const fixture = await createFixture([
      event('tool.result', {
        toolName: 'db.query_readonly',
        rowCount: 5,
        rows: [{ id: 1 }]
      })
    ]);

    const text = pageText(fixture);
    expect(text).toContain('db.query_readonly');
    expect(text).toContain('Rows');
    expect(text).toContain('5');
    expect(text).not.toContain('Citations');
  });

  it('renders db.get_schema_summary tables without row defaults', async () => {
    const fixture = await createFixture([
      event('tool.result', {
        toolName: 'db.get_schema_summary',
        tableCount: 1,
        tableNames: ['Orders']
      })
    ]);

    const text = pageText(fixture);
    expect(text).toContain('db.get_schema_summary');
    expect(text).toContain('Tables');
    expect(text).toContain('1');
    expect(text).toContain('Orders');
    expect(text).not.toContain('Rows');
  });

  it('renders failed tool results without row and citation counters', async () => {
    const fixture = await createFixture([
      event('tool.result', {
        toolName: 'db.query_readonly',
        success: false,
        attempt: 1,
        code: 'QUERY_VALIDATION_FAILED',
        message: 'StructuredQuery failed validation.'
      })
    ]);

    const text = pageText(fixture);
    expect(text).toContain('db.query_readonly');
    expect(text).toContain('Status');
    expect(text).toContain('Failed');
    expect(text).toContain('Attempt');
    expect(text).toContain('1');
    expect(text).toContain('QUERY_VALIDATION_FAILED');
    expect(text).toContain('StructuredQuery failed validation.');
    expect(text).not.toContain('Rows');
    expect(text).not.toContain('Citations');
  });

  it('withholds unsafe failed tool result details', async () => {
    const fixture = await createFixture([
      event('tool.result', {
        toolName: 'db.query_readonly',
        success: false,
        code: 'QUERY_VALIDATION_FAILED',
        message: 'SELECT * FROM Orders with database password leaked in stack trace'
      })
    ]);

    const text = pageText(fixture);
    expect(text).toContain('Tool failure details were withheld.');
    expect(text).not.toContain('SELECT *');
    expect(text).not.toContain('database password');
    expect(text).not.toContain('stack trace');
  });

  it('renders tool retry events as dedicated correction cards', async () => {
    const fixture = await createFixture([
      event('tool.retry', {
        toolName: 'db.query_readonly',
        attempt: 2,
        maxAttempts: 2,
        reason: 'schema_correction',
        message: 'Retrying db.query_readonly with a schema-corrected StructuredQuery.'
      })
    ]);

    const text = pageText(fixture);
    expect(text).toContain('tool.retry');
    expect(text).toContain('Retry');
    expect(text).toContain('db.query_readonly');
    expect(text).toContain('2 / 2');
    expect(text).toContain('schema_correction');
    expect(text).toContain('Retrying db.query_readonly with a schema-corrected StructuredQuery.');
  });

  it('renders assistant.message summary metrics when present', async () => {
    const fixture = await createFixture([
      event('assistant.message', {
        message: 'Delayed orders need escalation.',
        summary: {
          sqlRowCount: 5,
          documentResultCount: 4,
          citationCount: 1
        },
        citations: [{ citationId: 'doc-1', sourceName: 'nexus-demo-policy.txt' }]
      })
    ]);

    const text = pageText(fixture);
    expect(text).toContain('SQL rows');
    expect(text).toContain('5');
    expect(text).toContain('Doc results');
    expect(text).toContain('4');
    expect(text).toContain('Citations');
    expect(text).toContain('1');
    expect(text).toContain('Delayed orders need escalation.');
  });

  it('keeps approval-required trace behavior', async () => {
    const fixture = await createFixture([
      event('approval.required', {
        approvalId: 'approval-1',
        toolName: 'github.create_issue',
        riskSummary: 'Creates a GitHub issue.',
        params: {
          repo: 'example/repo',
          title: 'Delayed orders',
          labels: ['risk']
        }
      })
    ]);

    const text = pageText(fixture);
    expect(text).toContain('approval.required');
    expect(text).toContain('Approval required');
    expect(text).toContain('github.create_issue');
    expect(text).toContain('approval-1');
    expect(text).toContain('example/repo');
    expect(text).toContain('Delayed orders');
    expect(text).toContain('risk');
    expect(text).toContain('No external action has been executed.');
  });
});

async function createFixture(events: unknown[]) {
  await TestBed.configureTestingModule({
    imports: [TraceTimelineComponent]
  }).compileComponents();

  const fixture = TestBed.createComponent(TraceTimelineComponent);
  fixture.componentRef.setInput('events', events);
  fixture.detectChanges();
  await fixture.whenStable();
  return fixture;
}

function event(eventType: string, payload: unknown) {
  return {
    eventType,
    correlationId: '11111111-1111-1111-1111-111111111111',
    timestampUtc: '2026-04-02T03:00:00Z',
    payload
  };
}

function pageText(fixture: { nativeElement: HTMLElement }): string {
  return fixture.nativeElement.textContent ?? '';
}
