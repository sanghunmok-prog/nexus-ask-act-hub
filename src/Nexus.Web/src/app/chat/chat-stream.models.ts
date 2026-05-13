export interface ChatStreamEnvelope<TPayload = ChatStreamPayload> {
  eventType: string;
  correlationId: string;
  timestampUtc: string;
  payload: TPayload;
}

export type ChatStreamPayload =
  | WorkflowStartedPayload
  | ToolCallPayload
  | ToolRetryPayload
  | ToolResultPayload
  | ApprovalRequiredPayload
  | AssistantMessagePayload
  | ErrorPayload
  | DonePayload
  | Record<string, unknown>;

export interface WorkflowStartedPayload {
  prompt?: string;
}

export interface ToolCallPayload {
  toolName?: string;
  sanitizedArgs?: Record<string, unknown>;
  requiresApproval?: boolean;
}

export interface ToolResultPayload {
  toolName?: string;
  success?: boolean;
  attempt?: number;
  code?: string;
  message?: string;
  rowCount?: number;
  citationCount?: number;
  summary?: string;
}

export interface ToolRetryPayload {
  toolName?: string;
  attempt?: number;
  maxAttempts?: number;
  reason?: string;
  message?: string;
}

export interface ApprovalRequiredPayload {
  approvalId?: string;
  toolName?: string;
  riskSummary?: string;
  params?: {
    repo?: string;
    title?: string;
    labels?: string[];
  };
}

export interface Citation {
  citationId?: string;
  sourceName?: string;
  snippet?: string;
}

export interface AssistantMessagePayload {
  message?: string;
  citations?: Citation[];
}

export interface ErrorPayload {
  code?: string;
  message?: string;
  retryable?: boolean;
}

export interface DonePayload {
  success?: boolean;
}
