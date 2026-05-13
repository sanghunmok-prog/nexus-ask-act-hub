export interface PendingApprovalsResponse {
  approvals: PendingApproval[];
}

export interface PendingApproval {
  approvalId: string;
  correlationId: string;
  requestedAtUtc: string;
  requestedByUserId: string;
  status: string;
  toolName: string;
  paramsHash: string;
  params: ApprovalPublicParams;
  riskSummary: string;
}

export interface ApprovalPublicParams {
  repo: string;
  title: string;
  labels: string[];
}

export interface ApprovalDecisionResponse {
  approvalId: string;
  status: string;
  checkpointStatus: string;
  resumeAvailable: boolean;
  message: string;
}

export interface ApprovalErrorResponse {
  code?: string;
  message?: string;
  errors?: string[];
}
