import { Component, Input } from '@angular/core';
import { DateLabelPipe } from '../date-label.pipe';
import {
  AssistantMessagePayload,
  ChatStreamEnvelope,
  ToolCallPayload,
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
                  <div><strong>Tool:</strong> {{ toolCall(event).toolName || 'unknown' }}</div>
                  <pre>{{ pretty(toolCall(event).sanitizedArgs || {}) }}</pre>
                </div>
              }
              @case ('tool.result') {
                <div class="event__body">
                  <div><strong>Tool:</strong> {{ toolResult(event).toolName || 'unknown' }}</div>
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

  assistantMessage(event: ChatStreamEnvelope): AssistantMessagePayload {
    return event.payload as AssistantMessagePayload;
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
}
