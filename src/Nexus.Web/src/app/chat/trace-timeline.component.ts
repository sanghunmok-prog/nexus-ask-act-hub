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
                    <dl>
                      <div>
                        <dt>Rows</dt>
                        <dd>{{ toolResult(event).rowCount ?? 0 }}</dd>
                      </div>
                      <div>
                        <dt>Citations</dt>
                        <dd>{{ toolResult(event).citationCount ?? 0 }}</dd>
                      </div>
                    </dl>
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
                      <dt>Attempt</dt>
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
}
