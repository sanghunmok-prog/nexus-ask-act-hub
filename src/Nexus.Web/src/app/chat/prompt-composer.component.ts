import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-prompt-composer',
  imports: [FormsModule],
  template: `
    <form class="composer" (ngSubmit)="submit()">
      <label class="composer__label" for="prompt">Prompt</label>
      <textarea
        id="prompt"
        name="prompt"
        [(ngModel)]="prompt"
        [disabled]="active"
        rows="4"
        placeholder="Ask about delayed shipments and policy guidance"
      ></textarea>
      <div class="composer__actions">
        <span class="composer__state" aria-live="polite">{{ active ? 'Streaming...' : 'Ready' }}</span>
        <button type="submit" [disabled]="active || !prompt.trim()">Send</button>
      </div>
    </form>
  `,
  styleUrl: './prompt-composer.component.css'
})
export class PromptComposerComponent {
  @Input() active = false;
  @Output() promptSubmitted = new EventEmitter<string>();

  prompt = 'Show delayed shipments and cite the relevant policy.';

  submit(): void {
    const value = this.prompt.trim();
    if (!value || this.active) {
      return;
    }

    this.promptSubmitted.emit(value);
  }
}
