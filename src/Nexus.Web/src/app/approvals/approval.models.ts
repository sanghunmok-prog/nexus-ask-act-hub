export interface PendingApprovalsResponse {
  approvals: PendingApproval[];
}

export interface ReadyApprovalsResponse {
  approvals: ReadyApproval[];
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
  body?: string;
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
  errorCode?: string;
  message?: string;
  errors?: string[];
}

export interface ReadyApproval {
  approvalId: string;
  correlationId: string;
  checkpointId: string;
  checkpointStatus: string;
  approvedAtUtc?: string;
  approvedByUserId?: string;
  toolName: string;
  paramsHash: string;
  params: ApprovalPublicParams;
  riskSummary: string;
  executionAvailable: boolean;
}

export interface ApprovalExecutionResponse {
  approvalId: string;
  checkpointId: string;
  toolName: string;
  status: string;
  checkpointStatus: string;
  issueNumber?: number;
  issueUrl?: string;
  errorCode?: string;
  message: string;
}
