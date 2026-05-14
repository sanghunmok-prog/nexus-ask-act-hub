import { Component, Input, OnChanges, OnInit, SimpleChanges, inject, signal } from '@angular/core';
import { DateLabelPipe } from '../date-label.pipe';
import { ApprovalDecisionResponse, ApprovalExecutionResponse, PendingApproval, ReadyApproval } from './approval.models';
import { ApprovalService } from './approval.service';

@Component({
  selector: 'app-approval-panel',
  imports: [DateLabelPipe],
  template: `
    <section class="approvals" aria-label="Pending approvals">
      <div class="approvals__header">
        <div>
          <h2>Pending Approvals</h2>
          <p>No external action has been executed.</p>
        </div>
        <button type="button" (click)="refresh()" [disabled]="loading() || actionInFlight()">
          Refresh
        </button>
      </div>

      @if (successMessage()) {
        <p class="notice" role="status">{{ successMessage() }}</p>
      }

      @if (errorMessage()) {
        <p class="error" role="alert">{{ errorMessage() }}</p>
      }

      @if (loading()) {
        <p class="muted">Loading approvals...</p>
      } @else if (approvals().length === 0) {
        <p class="empty">No pending approvals.</p>
      } @else {
        <div class="approval-list">
          @for (approval of approvals(); track approval.approvalId) {
            <article class="approval-card">
              <div class="approval-card__top">
                <span class="badge">{{ approval.status }}</span>
                <time>{{ approval.requestedAtUtc | dateLabel }}</time>
              </div>

              <dl>
                <div>
                  <dt>Repo</dt>
                  <dd>{{ approval.params.repo }}</dd>
                </div>
                <div>
                  <dt>Title</dt>
                  <dd>{{ approval.params.title }}</dd>
                </div>
                <div>
                  <dt>Labels</dt>
                  <dd>{{ labelText(approval.params.labels) }}</dd>
                </div>
                <div>
                  <dt>Risk</dt>
                  <dd>{{ approval.riskSummary }}</dd>
                </div>
                <div>
                  <dt>Requested</dt>
                  <dd>{{ approval.requestedAtUtc }}</dd>
                </div>
                <div>
                  <dt>Requested By</dt>
                  <dd>{{ approval.requestedByUserId }}</dd>
                </div>
                <div>
                  <dt>Approval ID</dt>
                  <dd class="mono">{{ approval.approvalId }}</dd>
                </div>
              </dl>

              <p class="execution-note">No external action has been executed.</p>

              <div class="approval-card__actions">
                <button
                  type="button"
                  class="primary"
                  (click)="approve(approval.approvalId)"
                  [disabled]="actionInFlight()"
                >
                  Approve
                </button>
                <button
                  type="button"
                  class="secondary"
                  (click)="reject(approval.approvalId)"
                  [disabled]="actionInFlight()"
                >
                  Reject
                </button>
              </div>
            </article>
          }
        </div>
      }
    </section>

    <section class="approvals" aria-label="Ready approvals">
      <div class="approvals__header">
        <div>
          <h2>Ready to Execute</h2>
          <p>Approved action ready to execute.</p>
        </div>
      </div>

      @if (loading()) {
        <p class="muted">Loading ready actions...</p>
      } @else if (readyApprovals().length === 0) {
        <p class="empty">No approved actions ready to execute.</p>
      } @else {
        <div class="approval-list">
          @for (approval of readyApprovals(); track approval.approvalId) {
            <article class="approval-card approval-card--ready">
              <div class="approval-card__top">
                <span class="badge badge--ready">{{ approval.checkpointStatus }}</span>
                <time>{{ approval.approvedAtUtc | dateLabel }}</time>
              </div>

              <dl>
                <div>
                  <dt>Repo</dt>
                  <dd>{{ approval.params.repo }}</dd>
                </div>
                <div>
                  <dt>Title</dt>
                  <dd>{{ approval.params.title }}</dd>
                </div>
                <div>
                  <dt>Labels</dt>
                  <dd>{{ labelText(approval.params.labels) }}</dd>
                </div>
                <div>
                  <dt>Approved By</dt>
                  <dd>{{ approval.approvedByUserId || 'demo-user' }}</dd>
                </div>
                <div>
                  <dt>Approval ID</dt>
                  <dd class="mono">{{ approval.approvalId }}</dd>
                </div>
              </dl>

              <p class="execution-note">Approved action ready to execute.</p>

              <div class="approval-card__actions">
                <button
                  type="button"
                  class="primary"
                  (click)="execute(approval.approvalId)"
                  [disabled]="actionInFlight() || !approval.executionAvailable"
                >
                  Execute
                </button>
              </div>
            </article>
          }
        </div>
      }
    </section>
  `,
  styleUrl: './approval-panel.component.css'
})
export class ApprovalPanelComponent implements OnInit, OnChanges {
  @Input() refreshVersion = 0;

  private readonly approvalService = inject(ApprovalService);

  protected readonly approvals = signal<PendingApproval[]>([]);
  protected readonly readyApprovals = signal<ReadyApproval[]>([]);
  protected readonly loading = signal(false);
  protected readonly decidingApprovalId = signal<string | null>(null);
  protected readonly executingApprovalId = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);
  protected readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    void this.loadApprovals();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['refreshVersion'] && !changes['refreshVersion'].firstChange) {
      void this.loadApprovals();
    }
  }

  decisionInFlight(): boolean {
    return this.decidingApprovalId() !== null;
  }

  actionInFlight(): boolean {
    return this.decidingApprovalId() !== null || this.executingApprovalId() !== null;
  }

  refresh(): void {
    void this.loadApprovals();
  }

  approve(approvalId: string): void {
    void this.recordDecision(approvalId, 'approve');
  }

  reject(approvalId: string): void {
    void this.recordDecision(approvalId, 'reject');
  }

  execute(approvalId: string): void {
    void this.executeApproval(approvalId);
  }

  labelText(labels: string[] | null | undefined): string {
    return labels && labels.length > 0 ? labels.join(', ') : 'None';
  }

  private async loadApprovals(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const [pending, ready] = await Promise.all([
        this.approvalService.getPending(),
        this.approvalService.getReady()
      ]);
      this.approvals.set(pending);
      this.readyApprovals.set(ready);
    } catch (error) {
      this.errorMessage.set(this.safeErrorMessage(error, 'Pending approvals could not be loaded.'));
    } finally {
      this.loading.set(false);
    }
  }

  private async recordDecision(approvalId: string, decision: 'approve' | 'reject'): Promise<void> {
    this.decidingApprovalId.set(approvalId);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    try {
      const response =
        decision === 'approve'
          ? await this.approvalService.approve(approvalId)
          : await this.approvalService.reject(approvalId);

      this.successMessage.set(this.decisionMessage(response));
      await this.loadApprovals();
    } catch (error) {
      this.errorMessage.set(this.safeErrorMessage(error, 'Approval decision could not be recorded.'));
    } finally {
      this.decidingApprovalId.set(null);
    }
  }

  private async executeApproval(approvalId: string): Promise<void> {
    this.executingApprovalId.set(approvalId);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    try {
      const response = await this.approvalService.execute(approvalId);
      this.successMessage.set(this.executionMessage(response));
      await this.loadApprovals();
    } catch (error) {
      this.errorMessage.set(this.safeErrorMessage(error, 'Approved action could not be executed.'));
    } finally {
      this.executingApprovalId.set(null);
    }
  }

  private decisionMessage(response: ApprovalDecisionResponse): string {
    return `${response.status}. Checkpoint status: ${response.checkpointStatus}. ${response.message}`;
  }

  private executionMessage(response: ApprovalExecutionResponse): string {
    const issue = response.issueNumber && response.issueUrl
      ? ` Issue #${response.issueNumber}: ${response.issueUrl}`
      : '';
    return `${response.message}${issue}`;
  }

  private safeErrorMessage(error: unknown, fallback: string): string {
    if (error instanceof Error && error.message.length <= 220) {
      return error.message;
    }

    return fallback;
  }
}
