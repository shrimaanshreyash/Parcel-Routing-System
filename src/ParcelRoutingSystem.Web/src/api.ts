/**
 * Describes the safe RFC 7807 fields returned when the API rejects a request.
 * Validation dictionaries remain optional because not every failure is a model error.
 */
interface ProblemDetails {
  title?: string
  detail?: string
  status?: number
  code?: string
  errorCode?: string
  errors?: Record<string, string[]>
  previousBatchId?: string
  previousImportedAtUtc?: string
}

/**
 * Carries one immutable server-owned routing result to operator views.
 */
export interface RoutingDecision {
  id: string
  weightKilograms: number
  declaredValueEuros: number
  destinationCountry: string
  intendedDepartment: string
  approvalState: string
  isInsuranceApproved: boolean
  ruleSetVersion: number
  matchedRuleIds: string[]
  reasons: string[]
  decidedAtUtc: string
  correlationId: string
  batchId: string | null
}

export interface ApprovalEvidence {
  id: string
  approvedBy: string
  approvedAtUtc: string
  correlationId: string
}

export interface RoutingDecisionDetails {
  decision: RoutingDecision
  approval: ApprovalEvidence | null
}

/**
 * Reports whether an idempotent routing request created or replayed a decision.
 */
export interface RouteParcelResult {
  decision: RoutingDecision
  wasReplay: boolean
}

/**
 * Defines one readable row from the active typed rule set.
 */
export interface ActiveRule {
  ruleId: string
  input: string
  condition: string
  outcome: string
  priority: number
}

/**
 * Groups the immutable active rules under their persisted version.
 */
export interface ActiveRuleSet {
  version: number
  status: string
  createdAtUtc: string
  createdBy: string
  activatedAtUtc: string | null
  mailUpperKilograms: number
  regularUpperKilograms: number
  insuranceThresholdEuros: number
  rules: ActiveRule[]
}

/**
 * Restricts historical reads to server-supported relative windows.
 */
export type OperationsTimeRange =
  | 'Recent'
  | 'Last24Hours'
  | 'Last7Days'
  | 'Last30Days'
  | 'Last12Months'
  | 'AllTime'

/**
 * Restricts decision-history filtering to server-owned department and approval
 * categories that remain explainable to operators.
 */
export type RoutingDecisionFilter =
  | 'All'
  | 'Mail'
  | 'Regular'
  | 'Heavy'
  | 'AwaitingApproval'
  | 'Approved'
  | 'ApprovalNotRequired'

/**
 * Restricts activity filtering to stable operational event families.
 */
export type ActivityCategory =
  | 'All'
  | 'Imports'
  | 'Routing'
  | 'Insurance'
  | 'Rules'

/**
 * Separates permanent import issues from transient durable queue work.
 */
export type ImportAttentionKind = 'Issues' | 'Queue'

/**
 * Carries one server-bounded page and stable navigation metadata.
 */
export interface PagedResponse<T> {
  items: T[]
  page: number
  pageSize: number
  totalItems: number
  totalPages: number
}

/**
 * Provides current operational counts and one decision-history page.
 */
export interface OperationsOverview {
  totalDecisions: number
  processedToday: number
  awaitingInsuranceApproval: number
  importIssues: number
  pendingBatchRows: number
  decisionRange: OperationsTimeRange
  decisionFilter: RoutingDecisionFilter
  decisionHistory: PagedResponse<RoutingDecision>
}

/**
 * Represents one privacy-safe persisted audit event.
 */
export interface ActivityRecord {
  id: string
  eventType: string
  subjectType: string
  subjectId: string
  actorId: string
  correlationId: string
  occurredAtUtc: string
  details: Record<string, string>
  relatedBatchId: string | null
  relatedDecisionId: string | null
}

/**
 * Represents one privacy-safe import row requiring operator visibility.
 */
export interface ImportAttentionItem {
  rowId: string
  batchId: string
  rowNumber: number
  status: string
  errorCode: string | null
  errorMessage: string | null
  attemptCount: number
  batchCreatedAtUtc: string
}

export interface BatchSummary {
  id: string
  fallbackDestinationCountry: string | null
  status: string
  totalRows: number
  completedRows: number
  failedRows: number
  awaitingInsuranceApproval: number
  createdAtUtc: string
  createdBy: string
}

export interface CurrentIdentity {
  actorId: string
  displayName: string
  roles: string[]
  isDevelopmentIdentity: boolean
}

export interface RuleSimulationDifference {
  sampleId: string
  currentDepartment: string
  proposedDepartment: string
  currentApprovalState: string
  proposedApprovalState: string
}

export interface RuleSimulation {
  candidateVersion: number
  sampleCount: number
  changedCount: number
  differences: RuleSimulationDifference[]
}

/**
 * Represents one independently retriable row in a durable XML batch.
 */
export interface BatchRow {
  id: string
  rowNumber: number
  weightKilograms: number
  declaredValueEuros: number
  destinationCountry: string
  countrySource: string
  status: string
  errorCode: string | null
  errorMessage: string | null
  attemptCount: number
  decision: RoutingDecision | null
}

/**
 * Carries durable batch progress and ordered row outcomes.
 */
export interface Batch {
  id: string
  wasCreated: boolean
  fallbackDestinationCountry: string | null
  status: string
  totalRows: number
  completedRows: number
  failedRows: number
  createdAtUtc: string
  rows: BatchRow[]
}

/**
 * Gives UI error boundaries a safe operator message and machine-readable code.
 */
export class ApiError extends Error {
  readonly status: number
  readonly errorCode?: string
  readonly previousBatchId?: string
  readonly previousImportedAtUtc?: string

  /**
   * Creates one sanitized client error without retaining the rejected request body.
   */
  constructor(
    message: string,
    status: number,
    errorCode?: string,
    previousBatchId?: string,
    previousImportedAtUtc?: string,
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.errorCode = errorCode
    this.previousBatchId = previousBatchId
    this.previousImportedAtUtc = previousImportedAtUtc
  }
}

/**
 * Creates an opaque operation identifier for idempotency and request tracing.
 * Browser storage is deliberately avoided so no credential or parcel data persists.
 */
function createOperationId(): string {
  return crypto.randomUUID()
}

/**
 * Converts a failed HTTP response into one controlled message suitable for an
 * operator. HTML gateway responses and unexpected payloads are never rendered.
 */
async function toApiError(response: Response): Promise<ApiError> {
  const genericMessage = `The service rejected this request (${response.status}).`

  try {
    const contentType = response.headers.get('content-type') ?? ''
    if (!contentType.includes('application/problem+json')
      && !contentType.includes('application/json')) {
      return new ApiError(genericMessage, response.status)
    }

    const problem = await response.json() as ProblemDetails
    const validationMessage = problem.errors
      ? Object.values(problem.errors).flat()[0]
      : undefined
    return new ApiError(
      validationMessage ?? problem.detail ?? problem.title ?? genericMessage,
      response.status,
      problem.code ?? problem.errorCode,
      problem.previousBatchId,
      problem.previousImportedAtUtc,
    )
  } catch {
    return new ApiError(genericMessage, response.status)
  }
}

/**
 * Sends one same-origin API request, enforces JSON responses, and centralizes
 * safe failure handling. Authentication remains the deployment host's concern.
 */
async function requestJson<T>(
  path: string,
  init: RequestInit = {},
): Promise<T> {
  const response = await fetch(path, {
    ...init,
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      'X-Correlation-ID': createOperationId(),
      ...init.headers,
    },
  })

  if (!response.ok) {
    throw await toApiError(response)
  }

  return await response.json() as T
}

/**
 * Probes the anonymous liveness endpoint so the shell reports the real API
 * connection state instead of presenting a hardcoded operational claim.
 */
export async function probeApiConnection(): Promise<boolean> {
  try {
    const response = await fetch('/health/live', {
      cache: 'no-store',
      credentials: 'same-origin',
      headers: {
        Accept: 'text/plain',
        'X-Correlation-ID': createOperationId(),
      },
    })
    return response.ok
  } catch {
    return false
  }
}

/**
 * Routes one parcel through the server-owned domain and persistence workflow.
 */
export async function routeParcel(input: {
  weightKilograms: number
  declaredValueEuros: number
  destinationCountry: string
  operatorReference?: string
}): Promise<RouteParcelResult> {
  return await requestJson<RouteParcelResult>('/api/parcels/route', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Idempotency-Key': createOperationId(),
    },
    body: JSON.stringify(input),
  })
}

/**
 * Appends one insurance approval to an existing high-value decision.
 */
export async function approveInsurance(decisionId: string): Promise<void> {
  await requestJson(`/api/approvals/${encodeURIComponent(decisionId)}/approve`, {
    method: 'POST',
    headers: {
      'Idempotency-Key': createOperationId(),
    },
  })
}

/**
 * Uploads the XML file itself as a bounded raw stream so the server can parse it
 * incrementally without a base64 or multipart copy.
 */
export async function importXmlManifest(
  file: File,
  fallbackCountry: string,
  confirmDuplicate = false,
): Promise<Batch> {
  const country = encodeURIComponent(fallbackCountry)
  return await requestJson<Batch>(
    `/api/batches/import-xml?fallbackCountry=${country}&confirmDuplicate=${confirmDuplicate}`,
    {
      method: 'POST',
      headers: {
        'Content-Type': 'application/xml',
        'X-Manifest-Name': file.name,
        'Idempotency-Key': createOperationId(),
      },
      body: file,
    },
  )
}

/**
 * Reads one durable batch during bounded client polling.
 */
export async function getBatch(batchId: string): Promise<Batch> {
  return await requestJson<Batch>(
    `/api/batches/${encodeURIComponent(batchId)}`,
  )
}

/**
 * Returns a bounded newest-first import history without retaining browser-only
 * batch state.
 */
export async function getRecentBatches(limit = 20): Promise<BatchSummary[]> {
  return await requestJson<BatchSummary[]>(`/api/batches?limit=${limit}`)
}

/**
 * Loads one immutable routing decision with its separate approval evidence.
 */
export async function getDecision(
  decisionId: string,
): Promise<RoutingDecisionDetails> {
  return await requestJson<RoutingDecisionDetails>(
    `/api/operations/decisions/${encodeURIComponent(decisionId)}`,
  )
}

/**
 * Loads one oldest-first page of decisions whose insurance hold is unresolved.
 */
export async function getAwaitingInsurance(
  page = 1,
): Promise<PagedResponse<RoutingDecision>> {
  return await requestJson<PagedResponse<RoutingDecision>>(
    `/api/operations/insurance/awaiting?page=${page}`,
  )
}

/**
 * Reads the validated server identity and allow-listed roles used to align
 * visible controls with the API authorization boundary.
 */
export async function getCurrentIdentity(): Promise<CurrentIdentity> {
  return await requestJson<CurrentIdentity>('/api/identity/current')
}

/**
 * Loads the server's active rules so the browser never duplicates thresholds.
 */
export async function getActiveRules(): Promise<ActiveRuleSet> {
  return await requestJson<ActiveRuleSet>('/api/rules/active')
}

/**
 * Returns bounded immutable rule-set history for monitoring and recovery.
 */
export async function getRuleVersions(limit = 20): Promise<ActiveRuleSet[]> {
  return await requestJson<ActiveRuleSet[]>(`/api/rules?limit=${limit}`)
}

/**
 * Submits only the constrained decimal thresholds supported by the server rule
 * lifecycle; arbitrary expressions never reach the domain.
 */
export async function createRuleDraft(input: {
  version: number
  mailUpperKilograms: number
  regularUpperKilograms: number
  insuranceThresholdEuros: number
}): Promise<ActiveRuleSet> {
  return await requestJson<ActiveRuleSet>('/api/rules/drafts', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Idempotency-Key': createOperationId(),
    },
    body: JSON.stringify(input),
  })
}

/**
 * Compares one stored draft with a bounded representative parcel set before an
 * administrator may activate it.
 */
export async function simulateRuleSet(
  version: number,
  samples: Array<{
    sampleId: string
    weightKilograms: number
    declaredValueEuros: number
    destinationCountry: string
  }>,
): Promise<RuleSimulation> {
  return await requestJson<RuleSimulation>(
    `/api/rules/${version}/simulate`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ samples }),
    },
  )
}

/**
 * Shares the authenticated activation and rollback request shape while keeping
 * both lifecycle actions explicit at the exported API boundary.
 */
async function changeActiveRuleSet(
  version: number,
  action: 'activate' | 'rollback',
): Promise<ActiveRuleSet> {
  return await requestJson<ActiveRuleSet>(
    `/api/rules/${version}/${action}`,
    {
      method: 'POST',
      headers: { 'Idempotency-Key': createOperationId() },
    },
  )
}

/**
 * Atomically makes one validated immutable version the active policy.
 */
export async function activateRuleSet(version: number): Promise<ActiveRuleSet> {
  return await changeActiveRuleSet(version, 'activate')
}

/**
 * Reactivates a prior valid version without modifying historical decisions.
 */
export async function rollbackRuleSet(version: number): Promise<ActiveRuleSet> {
  return await changeActiveRuleSet(version, 'rollback')
}

/**
 * Loads current persisted operational counters and recent decisions.
 */
export async function getOperationsOverview(
  range: OperationsTimeRange = 'Recent',
  page = 1,
  filter: RoutingDecisionFilter = 'All',
): Promise<OperationsOverview> {
  return await requestJson<OperationsOverview>(
    `/api/operations/overview?range=${range}&page=${page}&filter=${filter}`,
  )
}

/**
 * Loads a server-bounded newest-first privacy-safe activity page.
 */
export async function getActivity(
  range: OperationsTimeRange = 'Recent',
  page = 1,
  category: ActivityCategory = 'All',
): Promise<PagedResponse<ActivityRecord>> {
  return await requestJson<PagedResponse<ActivityRecord>>(
    `/api/operations/activity?range=${range}&page=${page}&category=${category}`,
  )
}

/**
 * Loads the exact durable rows represented by the Overview issue or queue KPI.
 */
export async function getImportAttention(
  kind: ImportAttentionKind,
  page = 1,
): Promise<PagedResponse<ImportAttentionItem>> {
  return await requestJson<PagedResponse<ImportAttentionItem>>(
    `/api/operations/import-attention?kind=${kind}&page=${page}`,
  )
}
