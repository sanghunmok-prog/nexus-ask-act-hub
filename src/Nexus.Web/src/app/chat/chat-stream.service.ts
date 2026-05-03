import { Injectable } from '@angular/core';
import { ChatStreamEnvelope } from './chat-stream.models';

@Injectable({ providedIn: 'root' })
export class ChatStreamService {
  async *stream(prompt: string, signal?: AbortSignal): AsyncGenerator<ChatStreamEnvelope> {
    const response = await fetch('/api/chat/stream', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Accept: 'text/event-stream'
      },
      body: JSON.stringify({ prompt }),
      signal
    });

    if (!response.ok) {
      throw new Error(`Chat stream failed with HTTP ${response.status}`);
    }

    if (!response.body) {
      throw new Error('Chat stream response did not include a body.');
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    try {
      while (true) {
        const { value, done } = await reader.read();
        buffer += decoder.decode(value ?? new Uint8Array(), { stream: !done });

        const lines = buffer.split(/\r?\n/);
        buffer = lines.pop() ?? '';

        for (const line of lines) {
          const envelope = this.parseSseLine(line);
          if (envelope) {
            yield envelope;
          }
        }

        if (done) {
          const envelope = this.parseSseLine(buffer);
          if (envelope) {
            yield envelope;
          }
          break;
        }
      }
    } finally {
      reader.releaseLock();
    }
  }

  private parseSseLine(line: string): ChatStreamEnvelope | null {
    if (!line.startsWith('data: ')) {
      return null;
    }

    const json = line.slice(6).trim();
    if (!json || json === '[DONE]') {
      return null;
    }

    return JSON.parse(json) as ChatStreamEnvelope;
  }
}
