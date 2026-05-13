import { TestBed } from '@angular/core/testing';
import { TraceTimelineComponent } from './trace-timeline.component';

describe('TraceTimelineComponent', () => {
  it('renders failed tool results without row and citation counters', async () => {
    const fixture = await createFixture([
      {
        eventType: 'tool.result',
        correlationId: '11111111-1111-1111-1111-111111111111',
        timestampUtc: '2026-04-02T03:00:00Z',
        payload: {
          toolName: 'db.query_readonly',
          success: false,
          attempt: 1,
          code: 'QUERY_VALIDATION_FAILED',
          message: 'StructuredQuery failed validation.'
        }
      }
    ]);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
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
      {
        eventType: 'tool.result',
        correlationId: '11111111-1111-1111-1111-111111111111',
        timestampUtc: '2026-04-02T03:00:00Z',
        payload: {
          toolName: 'db.query_readonly',
          success: false,
          code: 'QUERY_VALIDATION_FAILED',
          message: 'SELECT * FROM Orders with database password leaked in stack trace'
        }
      }
    ]);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Tool failure details were withheld.');
    expect(text).not.toContain('SELECT *');
    expect(text).not.toContain('database password');
    expect(text).not.toContain('stack trace');
  });

  it('renders tool retry events as dedicated correction cards', async () => {
    const fixture = await createFixture([
      {
        eventType: 'tool.retry',
        correlationId: '11111111-1111-1111-1111-111111111111',
        timestampUtc: '2026-04-02T03:00:00Z',
        payload: {
          toolName: 'db.query_readonly',
          attempt: 2,
          maxAttempts: 2,
          reason: 'schema_correction',
          message: 'Retrying db.query_readonly with a schema-corrected StructuredQuery.'
        }
      }
    ]);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('tool.retry');
    expect(text).toContain('Retry');
    expect(text).toContain('db.query_readonly');
    expect(text).toContain('2 / 2');
    expect(text).toContain('schema_correction');
    expect(text).toContain('Retrying db.query_readonly with a schema-corrected StructuredQuery.');
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
