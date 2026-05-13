import { Injectable } from '@angular/core';
import {
  ApprovalDecisionResponse,
  ApprovalErrorResponse,
  PendingApproval,
  PendingApprovalsResponse
} from './approval.models';

@Injectable({ providedIn: 'root' })
export class ApprovalService {
  async getPending(signal?: AbortSignal): Promise<PendingApproval[]> {
    const response = await fetch('/api/approvals/pending', {
      method: 'GET',
      headers: {
        Accept: 'application/json'
      },
      signal
    });

    const body = await this.readJson<PendingApprovalsResponse | ApprovalErrorResponse>(response);
    if (!response.ok) {
      throw new Error(this.errorMessage(response.status, body));
    }

    return 'approvals' in body && Array.isArray(body.approvals) ? body.approvals : [];
  }

  approve(approvalId: string, signal?: AbortSignal): Promise<ApprovalDecisionResponse> {
    return this.decide(approvalId, 'approve', signal);
  }

  reject(approvalId: string, signal?: AbortSignal): Promise<ApprovalDecisionResponse> {
    return this.decide(approvalId, 'reject', signal);
  }

  private async decide(
    approvalId: string,
    decision: 'approve' | 'reject',
    signal?: AbortSignal
  ): Promise<ApprovalDecisionResponse> {
    const response = await fetch(`/api/approvals/${encodeURIComponent(approvalId)}/${decision}`, {
      method: 'POST',
      headers: {
        Accept: 'application/json'
      },
      signal
    });

    const body = await this.readJson<ApprovalDecisionResponse | ApprovalErrorResponse>(response);
    if (!response.ok) {
      throw new Error(this.errorMessage(response.status, body));
    }

    return body as ApprovalDecisionResponse;
  }

  private async readJson<T>(response: Response): Promise<T> {
    try {
      return (await response.json()) as T;
    } catch {
      return {} as T;
    }
  }

  private errorMessage(status: number, body: unknown): string {
    const message = this.propertyValue(body, 'message');
    const code = this.propertyValue(body, 'code');

    if (message && this.isSafeMessage(message)) {
      return code ? `${code}: ${message}` : message;
    }

    return `Approval request failed with HTTP ${status}.`;
  }

  private isSafeMessage(message: string): boolean {
    const unsafePatterns = [/stack/i, /exception/i, /connection string/i, /password/i, /secret/i, /\bat\s+\S+\(/i];
    return message.length <= 180 && !unsafePatterns.some((pattern) => pattern.test(message));
  }

  private propertyValue(body: unknown, propertyName: string): string | undefined {
    if (!body || typeof body !== 'object' || !(propertyName in body)) {
      return undefined;
    }

    const value = (body as Record<string, unknown>)[propertyName];
    return typeof value === 'string' ? value : undefined;
  }
}
