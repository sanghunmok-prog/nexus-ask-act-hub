import { Component, computed, inject, signal } from '@angular/core';
import { AssistantMessagePayload, ChatStreamEnvelope } from './chat/chat-stream.models';
import { ChatStreamService } from './chat/chat-stream.service';
import { PromptComposerComponent } from './chat/prompt-composer.component';
import { TraceTimelineComponent } from './chat/trace-timeline.component';

@Component({
  selector: 'app-root',
  imports: [PromptComposerComponent, TraceTimelineComponent],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  private readonly chatStream = inject(ChatStreamService);
  private abortController: AbortController | null = null;

  protected readonly events = signal<ChatStreamEnvelope[]>([]);
  protected readonly active = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly finalMessage = computed(() => {
    const assistantEvents = this.events().filter((event) => event.eventType === 'assistant.message');
    const latest = assistantEvents.at(-1);
    return latest?.payload as AssistantMessagePayload | undefined;
  });

  async send(prompt: string): Promise<void> {
    this.abortController?.abort();
    this.abortController = new AbortController();
    this.events.set([]);
    this.error.set(null);
    this.active.set(true);

    try {
      for await (const event of this.chatStream.stream(prompt, this.abortController.signal)) {
        this.events.update((events) => [...events, event]);
      }
    } catch (error) {
      if (!this.abortController.signal.aborted) {
        this.error.set(error instanceof Error ? error.message : 'The chat stream failed.');
      }
    } finally {
      this.active.set(false);
      this.abortController = null;
    }
  }
}
