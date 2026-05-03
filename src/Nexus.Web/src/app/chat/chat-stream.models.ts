export interface ChatStreamEnvelope<TPayload = ChatStreamPayload> {
  eventType: string;
  correlationId: string;
  timestampUtc: string;
  payload: TPayload;
}

export type ChatStreamPayload =
  | WorkflowStartedPayload
  | ToolCallPayload
  | ToolResultPayload
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
  rowCount?: number;
  citationCount?: number;
  summary?: string;
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
