import { Component, Input } from '@angular/core';
import { DateLabelPipe } from '../date-label.pipe';
import {
  ApprovalRequiredPayload,
  AssistantMessagePayload,
  ChatStreamEnvelope,
  ToolCallPayload,
  ToolRetryPayload,
  ToolResultPayload
} from './chat-stream.models';

@Component({
  selector: 'app-trace-timeline',
  imports: [DateLabelPipe],
  template: `
    <section class="timeline" aria-label="Trace timeline">
      @if (events.length === 0) {
        <p class="empty">Trace events will appear here as the response streams.</p>
      } @else {
        @for (event of events; track $index) {
          <article class="event">
            <div class="event__header">
              <span class="event__type">{{ event.eventType }}</span>
              <time>{{ event.timestampUtc | dateLabel }}</time>
            </div>

            @switch (event.eventType) {
              @case ('tool.call') {
                <div class="event__body">
                  <div class="tool-line">
                    <span><strong>Tool:</strong> {{ toolCall(event).toolName || 'unknown' }}</span>
                    @if (toolCall(event).requiresApproval) {
                      <span class="approval-badge">Approval required</span>
                    }
                  </div>
                  <pre>{{ pretty(toolCall(event).sanitizedArgs || {}) }}</pre>
                </div>
              }
              @case ('tool.result') {
                <div class="event__body" [class.failed-result]="toolResult(event).success === false">
                  <div><strong>Tool:</strong> {{ toolResult(event).toolName || 'unknown' }}</div>
                  @if (toolResult(event).success === false) {
                    <dl>
                      <div>
                        <dt>Status</dt>
                        <dd>Failed</dd>
                      </div>
                      @if (toolResult(event).attempt !== undefined) {
                        <div>
                          <dt>Attempt</dt>
                          <dd>{{ toolResult(event).attempt }}</dd>
                        </div>
                      }
                      @if (safeText(toolResult(event).code)) {
                        <div>
                          <dt>Code</dt>
                          <dd>{{ safeText(toolResult(event).code) }}</dd>
                        </div>
                      }
                    </dl>
                    @if (safeText(toolResult(event).message)) {
                      <p>{{ safeText(toolResult(event).message) }}</p>
                    }
                  } @else {
                    @if (summaryItems(event).length > 0) {
                      <dl>
                        @for (item of summaryItems(event); track $index) {
                          <div>
                            <dt>{{ item.label }}</dt>
                            <dd>{{ item.value }}</dd>
                          </div>
                        }
                      </dl>
                    }
                    @if (toolResult(event).summary) {
                      <p>{{ toolResult(event).summary }}</p>
                    }
                  }
                </div>
              }
              @case ('tool.retry') {
                <div class="event__body retry-event">
                  <div class="retry-event__header">
                    <span class="retry-badge">Retry</span>
                    <strong>{{ toolRetry(event).toolName || 'unknown tool' }}</strong>
                  </div>
                  <dl>
                    <div>
                      <dt>Retry</dt>
                      <dd>{{ toolRetry(event).attempt ?? 0 }} / {{ toolRetry(event).maxAttempts ?? 0 }}</dd>
                    </div>
                    <div>
                      <dt>Reason</dt>
                      <dd>{{ toolRetry(event).reason || 'unknown' }}</dd>
                    </div>
                  </dl>
                  <p>{{ toolRetry(event).message || 'Retrying tool call.' }}</p>
                </div>
              }
              @case ('assistant.message') {
                <div class="event__body">
                  @if (summaryItems(event).length > 0) {
                    <dl>
                      @for (item of summaryItems(event); track $index) {
                        <div>
                          <dt>{{ item.label }}</dt>
                          <dd>{{ item.value }}</dd>
                        </div>
                      }
                    </dl>
                  }
                  <p>{{ assistantMessage(event).message }}</p>
                  @if ((assistantMessage(event).citations?.length ?? 0) > 0) {
                    <ul class="citations">
                      @for (citation of assistantMessage(event).citations; track citation.citationId || $index) {
                        <li>
                          <strong>{{ citation.sourceName || citation.citationId || 'Citation' }}</strong>
                          @if (citation.snippet) {
                            <span>{{ citation.snippet }}</span>
                          }
                        </li>
                      }
                    </ul>
                  }
                </div>
              }
              @case ('approval.required') {
                <div class="event__body approval-required">
                  <div class="approval-required__header">
                    <span class="approval-badge">Approval required</span>
                    <strong>{{ approvalRequired(event).toolName || 'unknown tool' }}</strong>
                  </div>
                  <dl>
                    <div>
                      <dt>Approval ID</dt>
                      <dd>{{ approvalRequired(event).approvalId || 'unknown' }}</dd>
                    </div>
                    <div>
                      <dt>Repo</dt>
                      <dd>{{ approvalRequired(event).params?.repo || 'unknown' }}</dd>
                    </div>
                    <div>
                      <dt>Title</dt>
                      <dd>{{ approvalRequired(event).params?.title || 'unknown' }}</dd>
                    </div>
                    <div>
                      <dt>Labels</dt>
                      <dd>{{ labelText(approvalRequired(event).params?.labels) }}</dd>
                    </div>
                  </dl>
                  <p>{{ approvalRequired(event).riskSummary || 'Approval is required.' }}</p>
                  <p>No external action has been executed.</p>
                  <p>Use the pending approvals panel to approve or reject.</p>
                </div>
              }
              @case ('workflow.started') {
                <div class="event__body">
                  <p>{{ messageFromPayload(event.payload, 'Workflow started.') }}</p>
                </div>
              }
              @case ('done') {
                <div class="event__body">
                  <p>Stream completed.</p>
                </div>
              }
              @case ('error') {
                <div class="event__body event__body--error">
                  <p>{{ messageFromPayload(event.payload, 'The stream returned an error.') }}</p>
                </div>
              }
              @default {
                <div class="event__body">
                  <p>Event received.</p>
                </div>
              }
            }
          </article>
        }
      }
    </section>
  `,
  styleUrl: './trace-timeline.component.css'
})
export class TraceTimelineComponent {
  @Input() events: ChatStreamEnvelope[] = [];

  toolCall(event: ChatStreamEnvelope): ToolCallPayload {
    return event.payload as ToolCallPayload;
  }

  toolResult(event: ChatStreamEnvelope): ToolResultPayload {
    return event.payload as ToolResultPayload;
  }

  toolRetry(event: ChatStreamEnvelope): ToolRetryPayload {
    return event.payload as ToolRetryPayload;
  }

  assistantMessage(event: ChatStreamEnvelope): AssistantMessagePayload {
    return event.payload as AssistantMessagePayload;
  }

  approvalRequired(event: ChatStreamEnvelope): ApprovalRequiredPayload {
    return event.payload as ApprovalRequiredPayload;
  }

  summaryItems(event: ChatStreamEnvelope): TraceSummaryItem[] {
    if (event.eventType === 'tool.result') {
      return this.toolResultSummaryItems(this.toolResult(event));
    }

    if (event.eventType === 'assistant.message') {
      return this.assistantSummaryItems(this.assistantMessage(event));
    }

    return [];
  }

  labelText(labels: string[] | null | undefined): string {
    return labels && labels.length > 0 ? labels.join(', ') : 'None';
  }

  messageFromPayload(payload: unknown, fallback: string): string {
    if (payload && typeof payload === 'object' && 'message' in payload) {
      return String(payload.message);
    }

    if (payload && typeof payload === 'object' && 'prompt' in payload) {
      return String(payload.prompt);
    }

    return fallback;
  }

  pretty(value: unknown): string {
    return JSON.stringify(value ?? {}, null, 2);
  }

  safeText(value: string | null | undefined): string {
    if (!value) {
      return '';
    }

    const unsafePatterns = [
      /select\s+.+\s+from/i,
      /insert\s+into/i,
      /update\s+.+\s+set/i,
      /delete\s+from/i,
      /connection string/i,
      /password/i,
      /secret/i,
      /stack trace/i,
      /\bat\s+\S+\(/i
    ];

    if (unsafePatterns.some((pattern) => pattern.test(value))) {
      return 'Tool failure details were withheld.';
    }

    return value.length > 220 ? `${value.slice(0, 220).trimEnd()}...` : value;
  }

  private toolResultSummaryItems(payload: ToolResultPayload): TraceSummaryItem[] {
    if (payload.success === false) {
      return [];
    }

    switch (payload.toolName) {
      case 'docs.search':
        return this.docsSearchSummaryItems(payload);
      case 'docs.get_chunk':
        return this.docsGetChunkSummaryItems(payload);
      case 'db.get_schema_summary':
        return this.dbSchemaSummaryItems(payload);
      case 'db.query_readonly':
        return this.dbQueryReadonlySummaryItems(payload);
      default:
        return this.genericToolResultSummaryItems(payload);
    }
  }

  private docsSearchSummaryItems(payload: ToolResultPayload): TraceSummaryItem[] {
    const items: TraceSummaryItem[] = [
      { label: 'Results', value: this.docsSearchResultCount(payload) }
    ];
    const topResult = this.recordValue(payload, 'topResult');
    const topSource = this.stringValue(topResult, 'sourceName') || this.stringValue(topResult, 'title');

    if (topSource) {
      items.push({ label: 'Top', value: topSource });
    }

    return items;
  }

  private docsGetChunkSummaryItems(payload: ToolResultPayload): TraceSummaryItem[] {
    const items: TraceSummaryItem[] = [{ label: 'Status', value: 'Chunk loaded' }];
    const source = this.stringValue(payload, 'sourceName') || this.stringValue(payload, 'title');
    const chunkIndex = this.numberValue(payload, 'chunkIndex');

    if (source) {
      items.push({ label: 'Source', value: source });
    }

    if (chunkIndex !== undefined) {
      items.push({ label: 'Chunk', value: chunkIndex });
    }

    if (this.stringValue(payload, 'citationId')) {
      items.push({ label: 'Citation', value: 'Available' });
    }

    return items;
  }

  private dbSchemaSummaryItems(payload: ToolResultPayload): TraceSummaryItem[] {
    const items: TraceSummaryItem[] = [];
    const tableCount = this.numberValue(payload, 'tableCount');
    const tableNames = this.arrayValue(payload, 'tableNames').filter((name): name is string => typeof name === 'string');

    if (tableCount !== undefined) {
      items.push({ label: 'Tables', value: tableCount });
    }

    if (tableNames.length > 0) {
      items.push({ label: 'Table', value: tableNames.join(', ') });
    }

    return items;
  }

  private dbQueryReadonlySummaryItems(payload: ToolResultPayload): TraceSummaryItem[] {
    const rowCount = this.numberValue(payload, 'rowCount');
    return rowCount === undefined ? [] : [{ label: 'Rows', value: rowCount }];
  }

  private genericToolResultSummaryItems(payload: ToolResultPayload): TraceSummaryItem[] {
    const items: TraceSummaryItem[] = [];
    const rowCount = this.numberValue(payload, 'rowCount');
    const citationCount = this.numberValue(payload, 'citationCount');

    if (rowCount !== undefined) {
      items.push({ label: 'Rows', value: rowCount });
    }

    if (citationCount !== undefined) {
      items.push({ label: 'Citations', value: citationCount });
    }

    return items;
  }

  private assistantSummaryItems(payload: AssistantMessagePayload): TraceSummaryItem[] {
    const summary = this.recordValue(payload, 'summary');
    const items: TraceSummaryItem[] = [];
    const sqlRowCount = this.numberValue(summary, 'sqlRowCount');
    const documentResultCount = this.numberValue(summary, 'documentResultCount');
    const citationCount = this.numberValue(summary, 'citationCount');

    if (sqlRowCount !== undefined) {
      items.push({ label: 'SQL rows', value: sqlRowCount });
    }

    if (documentResultCount !== undefined) {
      items.push({ label: 'Doc results', value: documentResultCount });
    }

    if (citationCount !== undefined) {
      items.push({ label: 'Citations', value: citationCount });
    }

    return items;
  }

  private docsSearchResultCount(payload: ToolResultPayload): number {
    const directResultCount = this.numberValue(payload, 'resultCount');
    if (directResultCount !== undefined) {
      return directResultCount;
    }

    const result = this.recordValue(payload, 'result');
    const nestedResultCount = this.numberValue(result, 'resultCount');
    if (nestedResultCount !== undefined) {
      return nestedResultCount;
    }

    return this.arrayValue(result, 'results').length;
  }

  private recordValue(value: unknown, key: string): Record<string, unknown> | undefined {
    if (!value || typeof value !== 'object') {
      return undefined;
    }

    const nested = (value as Record<string, unknown>)[key];
    return nested && typeof nested === 'object' && !Array.isArray(nested) ? nested as Record<string, unknown> : undefined;
  }

  private arrayValue(value: unknown, key: string): unknown[] {
    if (!value || typeof value !== 'object') {
      return [];
    }

    const nested = (value as Record<string, unknown>)[key];
    return Array.isArray(nested) ? nested : [];
  }

  private numberValue(value: unknown, key: string): number | undefined {
    if (!value || typeof value !== 'object') {
      return undefined;
    }

    const nested = (value as Record<string, unknown>)[key];
    return typeof nested === 'number' && Number.isFinite(nested) ? nested : undefined;
  }

  private stringValue(value: unknown, key: string): string {
    if (!value || typeof value !== 'object') {
      return '';
    }

    const nested = (value as Record<string, unknown>)[key];
    return typeof nested === 'string' ? nested : '';
  }
}

interface TraceSummaryItem {
  label: string;
  value: string | number;
}
