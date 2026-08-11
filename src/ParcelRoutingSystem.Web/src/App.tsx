import { Fragment, useEffect, useMemo, useRef, useState } from 'react'
import type { ChangeEvent, FormEvent, ReactNode } from 'react'
import {
  ArrowRight,
  CaretDown,
  CaretRight,
  CheckCircle,
  ClockCounterClockwise,
  CloudCheck,
  Cube,
  CurrencyEur,
  FileArrowUp,
  FileText,
  Info,
  ListChecks,
  LockKey,
  MapPin,
  MagnifyingGlass,
  Package,
  PlusCircle,
  Scales,
  ShieldCheck,
  SlidersHorizontal,
  SquaresFour,
  WarningCircle,
} from '@phosphor-icons/react'
import isoCountries from 'i18n-iso-countries'
import englishCountries from 'i18n-iso-countries/langs/en.json'
import {
  ApiError,
  approveInsurance,
  activateRuleSet,
  createRuleDraft,
  getActiveRules,
  getActivity,
  getAwaitingInsurance,
  getBatch,
  getCurrentIdentity,
  getDecision,
  getImportAttention,
  getOperationsOverview,
  getRecentBatches,
  getRuleVersions,
  importXmlManifest,
  probeApiConnection,
  rollbackRuleSet,
  routeParcel,
  simulateRuleSet,
} from './api'
import type {
  ActiveRule,
  ActiveRuleSet,
  ActivityCategory,
  ActivityRecord,
  Batch,
  BatchRow,
  BatchSummary,
  CurrentIdentity,
  ImportAttentionItem,
  ImportAttentionKind,
  OperationsOverview,
  OperationsTimeRange,
  PagedResponse,
  RoutingDecision,
  RoutingDecisionDetails,
  RoutingDecisionFilter,
  RuleSimulation,
} from './api'
import './App.css'

isoCountries.registerLocale(englishCountries)

type View = 'overview' | 'parcel' | 'import' | 'insurance' | 'rules' | 'activity'
type CountryOption = [code: string, name: string]
type ApiConnectionState = 'connecting' | 'connected' | 'unavailable'
type ImportWorkspaceMode = 'new' | 'operations'

interface NavigationItem {
  id: View
  label: string
  icon: ReactNode
}

interface ParcelDraft {
  weight: string
  value: string
  country: string
  reference: string
}

const initialParcel: ParcelDraft = {
  weight: '',
  value: '',
  country: '',
  reference: '',
}

const navigationItems: NavigationItem[] = [
  { id: 'overview', label: 'Overview', icon: <SquaresFour aria-hidden /> },
  { id: 'parcel', label: 'New parcel', icon: <PlusCircle aria-hidden /> },
  { id: 'import', label: 'Import XML', icon: <FileArrowUp aria-hidden /> },
  { id: 'insurance', label: 'Insurance', icon: <ShieldCheck aria-hidden /> },
  { id: 'rules', label: 'Routing rules', icon: <SlidersHorizontal aria-hidden /> },
  { id: 'activity', label: 'Activity', icon: <ClockCounterClockwise aria-hidden /> },
]

const historyRangeOptions: Array<{
  value: OperationsTimeRange
  label: string
}> = [
  { value: 'Recent', label: 'Recent 10' },
  { value: 'Last24Hours', label: '24 hours' },
  { value: 'Last7Days', label: '7 days' },
  { value: 'Last30Days', label: '30 days' },
  { value: 'Last12Months', label: '12 months' },
  { value: 'AllTime', label: 'All time' },
]

const decisionFilterOptions: Array<{
  value: RoutingDecisionFilter
  label: string
}> = [
  { value: 'All', label: 'All decisions' },
  { value: 'Mail', label: 'Mail department' },
  { value: 'Regular', label: 'Regular department' },
  { value: 'Heavy', label: 'Heavy department' },
  { value: 'AwaitingApproval', label: 'Awaiting approval' },
  { value: 'Approved', label: 'Approved' },
  { value: 'ApprovalNotRequired', label: 'Approval not required' },
]

const activityCategoryOptions: Array<{
  value: ActivityCategory
  label: string
}> = [
  { value: 'All', label: 'All activity' },
  { value: 'Imports', label: 'XML imports' },
  { value: 'Routing', label: 'Routing decisions' },
  { value: 'Insurance', label: 'Insurance approvals' },
  { value: 'Rules', label: 'Rule changes' },
]

/**
 * Sorts ISO countries alphabetically so operators can scan a predictable list.
 */
function compareCountryNames(first: CountryOption, second: CountryOption) {
  return first[1].localeCompare(second[1])
}

/**
 * Builds the complete English ISO country list from the maintained package.
 */
function getCountryOptions(): CountryOption[] {
  return (Object.entries(
    isoCountries.getNames('en', { select: 'official' }),
  ) as CountryOption[]).sort(compareCountryNames)
}

const countryOptions = getCountryOptions()

/**
 * Presents the large ISO country list in a searchable popover that always opens
 * below its trigger instead of delegating placement to the browser's native
 * select popup.
 */
function CountrySelect({
  id,
  labelId,
  value,
  placeholder,
  onChange,
}: {
  id: string
  labelId: string
  value: string
  placeholder: string
  onChange: (countryCode: string) => void
}) {
  const [isOpen, setIsOpen] = useState(false)
  const [query, setQuery] = useState('')
  const rootRef = useRef<HTMLDivElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const selectedCountry = countryOptions.find(([code]) => code === value)
  const filteredCountries = useMemo(
    /**
     * Matches either the operator-facing country name or its stable ISO code.
     */
    function filterCountries() {
      const normalizedQuery = query.trim().toLocaleLowerCase()
      if (!normalizedQuery) return countryOptions
      return countryOptions.filter(([code, name]) =>
        name.toLocaleLowerCase().includes(normalizedQuery)
        || code.toLocaleLowerCase().includes(normalizedQuery))
    },
    [query],
  )

  useEffect(function closeCountryMenuOutsideItsBoundary() {
    if (!isOpen) return

    /**
     * Closes the country popover when a pointer interaction leaves its owned
     * control boundary.
     */
    function handlePointerDown(event: PointerEvent) {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) {
        setIsOpen(false)
        setQuery('')
      }
    }

    /**
     * Gives keyboard operators a predictable escape path and restores focus to
     * the country trigger.
     */
    function handleEscape(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setIsOpen(false)
        setQuery('')
        triggerRef.current?.focus()
      }
    }
    document.addEventListener('pointerdown', handlePointerDown)
    document.addEventListener('keydown', handleEscape)
    return function removeCountryMenuListeners() {
      document.removeEventListener('pointerdown', handlePointerDown)
      document.removeEventListener('keydown', handleEscape)
    }
  }, [isOpen])

  /**
   * Commits one valid ISO code and closes the temporary search surface.
   */
  function selectCountry(countryCode: string) {
    onChange(countryCode)
    setIsOpen(false)
    setQuery('')
    triggerRef.current?.focus()
  }

  return (
    <div className="country-picker" ref={rootRef}>
      <button
        id={`${id}-trigger`}
        ref={triggerRef}
        type="button"
        className="country-picker-trigger"
        aria-labelledby={`${labelId} ${id}-value`}
        aria-haspopup="listbox"
        aria-expanded={isOpen}
        aria-controls={`${id}-options`}
        onClick={() => setIsOpen(current => !current)}
      >
        <MapPin aria-hidden />
        <span id={`${id}-value`}>{selectedCountry?.[1] ?? placeholder}</span>
        <CaretDown aria-hidden />
      </button>
      {isOpen && (
        <div className="country-picker-menu">
          <label className="country-search">
            <MagnifyingGlass aria-hidden />
            <span className="sr-only">Search countries</span>
            <input
              type="search"
              value={query}
              onChange={event => setQuery(event.target.value)}
              placeholder="Search by country or ISO code"
              autoFocus
            />
          </label>
          <div
            id={`${id}-options`}
            className="country-options"
            role="listbox"
            aria-labelledby={labelId}
          >
            {filteredCountries.length > 0 ? (
              filteredCountries.map(([code, name]) => (
                <button
                  type="button"
                  className="country-option"
                  role="option"
                  aria-selected={code === value}
                  key={code}
                  onClick={() => selectCountry(code)}
                >
                  <span>{name}</span>
                  <small>{code}</small>
                </button>
              ))
            ) : (
              <p className="country-empty">No country matches that search.</p>
            )}
          </div>
        </div>
      )}
    </div>
  )
}

/**
 * Formats the operator's current date without shipping a stale hardcoded label.
 */
function getCurrentDateLabel() {
  return new Intl.DateTimeFormat('en-GB', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
  }).format(new Date())
}

/**
 * Formats a persisted UTC timestamp in the operator's local time zone.
 */
function formatTimestamp(value: string) {
  return new Intl.DateTimeFormat('en-GB', {
    day: '2-digit',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value))
}

/**
 * Converts unexpected client failures into one restrained operator message.
 */
function getErrorMessage(error: unknown) {
  return error instanceof Error
    ? error.message
    : 'The service could not complete this request.'
}

/**
 * Removes legacy framework parameter metadata from previously persisted safe
 * messages while new imports store only the authored operator explanation.
 */
function formatOperatorIssue(message: string | null) {
  return message
    ?.replace(/\s+\(Parameter '[^']+'\)$/, '')
    ?? 'This row could not be evaluated safely.'
}

/**
 * Converts the stable API approval enum into concise operator language.
 */
function formatApproval(decision: RoutingDecision) {
  if (decision.isInsuranceApproved) return 'Approved'
  return decision.approvalState === 'PendingInsuranceApproval'
    ? 'Awaiting insurance'
    : 'Not required'
}

/**
 * Converts durable batch lifecycle values into language that describes an
 * evaluation outcome rather than exposing persistence enum names.
 */
function formatBatchStatus(status: string) {
  switch (status) {
    case 'Completed':
      return 'Evaluation complete'
    case 'CompletedWithErrors':
      return 'Evaluated with issues'
    case 'Processing':
      return 'Evaluation in progress'
    case 'Pending':
      return 'Waiting to evaluate'
    default:
      return status
  }
}

/**
 * Separates operator actions from technical row states while retaining the
 * stable state in secondary details for support and audit work.
 */
function formatBatchRowStatus(status: string) {
  switch (status) {
    case 'Completed':
      return 'Evaluated'
    case 'ValidationFailed':
      return 'Needs correction'
    case 'ProcessingFailed':
      return 'Processing failed'
    case 'Processing':
      return 'Processing'
    case 'Pending':
      return 'Waiting'
    default:
      return status
  }
}

/**
 * Explains the next safe operator action for a failed row without suggesting
 * that an immutable historical batch can be edited in place.
 */
function getBatchRowRecovery(status: string) {
  return status === 'ValidationFailed'
    ? 'Correct this row in the source XML, then import a correction manifest.'
    : 'Resolve the processing dependency before importing this row again.'
}

/**
 * Resolves one alpha-2 code to a friendly operator name while keeping the code
 * visible for manifest reconciliation.
 */
function formatCountry(code: string) {
  if (code === '--') {
    return 'Unavailable'
  }

  return `${isoCountries.getName(code, 'en') ?? code} (${code})`
}

/**
 * Owns the operator-shell navigation while each view loads its own server state.
 */
function App() {
  const [activeView, setActiveView] = useState<View>('overview')
  const [selectedDecisionId, setSelectedDecisionId] = useState<string | null>(null)
  const [selectedBatchId, setSelectedBatchId] = useState<string | null>(null)
  const [selectedImportAttention, setSelectedImportAttention] =
    useState<ImportAttentionKind | null>(null)
  const [selectedRuleVersion, setSelectedRuleVersion] = useState<number | null>(null)
  const [identity, setIdentity] = useState<CurrentIdentity | null>(null)
  const [apiConnection, setApiConnection] =
    useState<ApiConnectionState>('connecting')
  // A successful approval invalidates several server read models. Incrementing
  // this token remounts only those bounded views so none retain a stale hold.
  const [operationsRevision, setOperationsRevision] = useState(0)

  useEffect(function loadIdentity() {
    let isCurrent = true
    getCurrentIdentity()
      .then(function applyIdentity(result) {
        if (isCurrent) setIdentity(result)
      })
      .catch(function ignoreIdentityFailure() {
        if (isCurrent) setIdentity(null)
      })
    return function cancelIdentityUpdate() {
      isCurrent = false
    }
  }, [])

  useEffect(
    /**
     * Rechecks the API liveness boundary periodically so a disconnected
     * reviewer runtime becomes visible without requiring a page refresh.
     */
    function monitorApiConnection() {
      let isCurrent = true

      /**
       * Applies only the latest health result and leaves stale asynchronous
       * probes unable to update an unmounted operator shell.
       */
      async function refreshApiConnection() {
        const isConnected = await probeApiConnection()
        if (isCurrent) {
          setApiConnection(isConnected ? 'connected' : 'unavailable')
        }
      }

      void refreshApiConnection()
      const intervalId = window.setInterval(refreshApiConnection, 15_000)
      return function stopApiConnectionMonitor() {
        isCurrent = false
        window.clearInterval(intervalId)
      }
    },
    [],
  )

  /**
   * Changes the visible workspace without reloading the browser and consumes
   * any Activity-only rule-version target so it cannot remain highlighted.
   */
  function handleNavigate(view: View) {
    setSelectedRuleVersion(null)
    if (view === 'import') {
      setSelectedBatchId(null)
      setSelectedImportAttention(null)
    }
    setActiveView(view)
  }

  /**
   * Opens one persisted server decision in the shared detail drawer.
   */
  function handleOpenDecision(decisionId: string) {
    setSelectedDecisionId(decisionId)
  }

  /**
   * Navigates to a persisted import and asks that view to restore its detail.
   */
  function handleOpenBatch(batchId: string) {
    setSelectedBatchId(batchId)
    setSelectedImportAttention(null)
    setActiveView('import')
  }

  /**
   * Opens the Import XML workspace at the concrete row list represented by one
   * Overview attention KPI.
   */
  function handleOpenImportAttention(kind: ImportAttentionKind) {
    setSelectedBatchId(null)
    setSelectedImportAttention(kind)
    setActiveView('import')
  }

  /**
   * Navigates to one immutable rule-set version so audit events restore the
   * exact lifecycle record instead of only opening the general rules page.
   */
  function handleOpenRuleVersion(version: number) {
    setSelectedRuleVersion(version)
    setActiveView('rules')
  }

  return (
    <div className="product-shell">
      <Sidebar
        activeView={activeView}
        apiConnection={apiConnection}
        onNavigate={handleNavigate}
      />
      <div className="workspace">
        <Topbar activeView={activeView} identity={identity} />
        <main className="workspace-scroll" id="main-content">
          {renderWorkspaceView(
            activeView,
            handleNavigate,
            handleOpenDecision,
            handleOpenBatch,
            handleOpenImportAttention,
            handleOpenRuleVersion,
            selectedBatchId,
            selectedImportAttention,
            selectedRuleVersion,
            identity,
            operationsRevision,
          )}
        </main>
      </div>
      {selectedDecisionId && (
        <DecisionDrawer
          decisionId={selectedDecisionId}
          identity={identity}
          onClose={() => setSelectedDecisionId(null)}
          onOpenBatch={handleOpenBatch}
          onApproved={() => setOperationsRevision(revision => revision + 1)}
        />
      )}
    </div>
  )
}

/**
 * Selects a known workspace view and safely defaults to the overview.
 */
function renderWorkspaceView(
  activeView: View,
  onNavigate: (view: View) => void,
  onOpenDecision: (decisionId: string) => void,
  onOpenBatch: (batchId: string) => void,
  onOpenImportAttention: (kind: ImportAttentionKind) => void,
  onOpenRuleVersion: (version: number) => void,
  selectedBatchId: string | null,
  selectedImportAttention: ImportAttentionKind | null,
  selectedRuleVersion: number | null,
  identity: CurrentIdentity | null,
  operationsRevision: number,
) {
  switch (activeView) {
    case 'parcel':
      return <NewParcel identity={identity} />
    case 'import':
      return (
        <ImportManifest
          key={`import-${operationsRevision}`}
          selectedBatchId={selectedBatchId}
          selectedAttentionKind={selectedImportAttention}
          onOpenDecision={onOpenDecision}
        />
      )
    case 'insurance':
      return (
        <InsuranceQueue
          key={`insurance-${operationsRevision}`}
          identity={identity}
          onOpenDecision={onOpenDecision}
        />
      )
    case 'rules':
      return (
        <Rules
          identity={identity}
          selectedVersion={selectedRuleVersion}
        />
      )
    case 'activity':
      return (
        <Activity
          key={`activity-${operationsRevision}`}
          onNavigate={onNavigate}
          onOpenDecision={onOpenDecision}
          onOpenBatch={onOpenBatch}
          onOpenRuleVersion={onOpenRuleVersion}
        />
      )
    case 'overview':
    default:
      return (
        <Overview
          key={`overview-${operationsRevision}`}
          onNavigate={onNavigate}
          onOpenDecision={onOpenDecision}
          onOpenImportAttention={onOpenImportAttention}
        />
      )
  }
}

/**
 * Renders product identity, navigation, and a live API connection state.
 */
function Sidebar({
  activeView,
  apiConnection,
  onNavigate,
}: {
  activeView: View
  apiConnection: ApiConnectionState
  onNavigate: (view: View) => void
}) {
  /**
   * Converts one navigation definition into an accessible button.
   */
  function renderNavigationItem(item: NavigationItem) {
    return (
      <button
        type="button"
        className={activeView === item.id ? 'nav-item is-active' : 'nav-item'}
        onClick={onNavigate.bind(null, item.id)}
        aria-current={activeView === item.id ? 'page' : undefined}
        key={item.id}
      >
        {item.icon}
        <span>{item.label}</span>
      </button>
    )
  }

  return (
    <aside className="sidebar">
      <div className="brand" aria-label="Parcel Routing System">
        <Cube className="brand-icon" weight="fill" aria-hidden />
        <span className="brand-copy">
          <strong>Parcel Routing</strong>
          <small>Operations system</small>
        </span>
      </div>
      <nav className="primary-nav" aria-label="Primary navigation">
        {navigationItems.map(renderNavigationItem)}
      </nav>
      <div className="sidebar-status">
        <span className="status-kicker">System status</span>
        <strong>Operational workspace</strong>
        <p>Decisions are calculated by versioned server-side rules.</p>
        <span
          className={`status-line is-${apiConnection}`}
          role="status"
          aria-live="polite"
        >
          <span className="live-dot" aria-hidden />
          {apiConnection === 'connected'
            ? 'API connected'
            : apiConnection === 'unavailable'
              ? 'API unavailable'
              : 'Connecting to API'}
        </span>
      </div>
    </aside>
  )
}

/**
 * Provides compact page context without fabricating a production identity.
 */
function Topbar({
  activeView,
  identity,
}: {
  activeView: View
  identity: CurrentIdentity | null
}) {
  /**
   * Matches the active view to its operator-facing label.
   */
  function findCurrentPage(item: NavigationItem) {
    return item.id === activeView
  }

  const pageName = navigationItems.find(findCurrentPage)?.label ?? 'Overview'

  return (
    <header className="topbar">
      <div className="breadcrumb">
        <span>Parcel routing</span>
        <CaretRight aria-hidden />
        <strong>{pageName}</strong>
      </div>
      <div className="environment-status">
        <CloudCheck aria-hidden />
        <span>{identity?.displayName ?? 'Authenticated reviewer'}</span>
      </div>
    </header>
  )
}

/**
 * Loads persisted counters and recent decisions for the operational landing page.
 */
function Overview({
  onNavigate,
  onOpenDecision,
  onOpenImportAttention,
}: {
  onNavigate: (view: View) => void
  onOpenDecision: (decisionId: string) => void
  onOpenImportAttention: (kind: ImportAttentionKind) => void
}) {
  const [overview, setOverview] = useState<OperationsOverview | null>(null)
  const [range, setRange] = useState<OperationsTimeRange>('Recent')
  const [decisionFilter, setDecisionFilter] =
    useState<RoutingDecisionFilter>('All')
  const [page, setPage] = useState(1)
  const [error, setError] = useState('')
  const [isLoading, setIsLoading] = useState(true)

  useEffect(
    /**
     * Retrieves counters and the selected server-owned history page while
     * ignoring late responses after navigation or filter changes.
     */
    function loadOverview() {
      let isCurrent = true
      setIsLoading(true)
      setError('')
      getOperationsOverview(range, page, decisionFilter)
        .then(function applyOverview(operations) {
          if (isCurrent) {
            setOverview(operations)
          }
        })
        .catch(function applyOverviewError(reason: unknown) {
          if (isCurrent) setError(getErrorMessage(reason))
        })
        .finally(function finishOverviewLoad() {
          if (isCurrent) setIsLoading(false)
        })
      return function cancelOverviewUpdate() {
        isCurrent = false
      }
    },
    [range, page, decisionFilter],
  )

  const decisions = overview?.decisionHistory.items ?? []

  /**
   * Starts each selected history window at its first page so an older page
   * number cannot produce a confusing empty state.
   */
  function handleRangeChange(nextRange: OperationsTimeRange) {
    setRange(nextRange)
    setPage(1)
  }

  /**
   * Applies one server-owned decision category and restarts paging so the first
   * visible page always belongs to the newly selected result set.
   */
  function handleDecisionFilterChange(nextFilter: RoutingDecisionFilter) {
    setDecisionFilter(nextFilter)
    setPage(1)
  }

  /**
   * Moves keyboard and pointer users directly to the persisted history already
   * represented by the total-decisions KPI.
   */
  function focusDecisionHistory() {
    document.getElementById('decision-history')?.scrollIntoView({
      behavior: 'smooth',
      block: 'start',
    })
  }

  /**
   * Renders one privacy-safe historical decision.
   */
  function renderDecision(decision: RoutingDecision) {
    return (
      <tr
        key={decision.id}
        className="clickable-row"
        aria-label={`Open decision ${decision.id.slice(0, 8)}`}
        onClick={() => onOpenDecision(decision.id)}
        tabIndex={0}
        onKeyDown={event => {
          if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault()
            onOpenDecision(decision.id)
          }
        }}
      >
        <td className="data-cell">{decision.id.slice(0, 8)}</td>
        <td>{decision.weightKilograms.toLocaleString()} kg</td>
        <td>€{decision.declaredValueEuros.toLocaleString()}</td>
        <td>{formatCountry(decision.destinationCountry)}</td>
        <td><strong>{decision.intendedDepartment}</strong></td>
        <td><ApprovalBadge decision={decision} /></td>
      </tr>
    )
  }

  return (
    <div className="page overview-page">
      <section className="page-heading heading-with-actions">
        <div>
          <p className="eyebrow">{getCurrentDateLabel()}</p>
          <h1>Routing workspace</h1>
          <p>Enter one parcel or process a legacy XML manifest.</p>
        </div>
        <div className="heading-actions">
          <button type="button" className="button button-secondary" onClick={onNavigate.bind(null, 'import')}>
            <FileArrowUp aria-hidden /> Import XML
          </button>
          <button type="button" className="button button-primary" onClick={onNavigate.bind(null, 'parcel')}>
            <PlusCircle aria-hidden /> Enter a parcel
          </button>
        </div>
      </section>

      <section className="metric-grid" aria-label="Current routing status">
        <Metric
          label="Routing decisions"
          value={overview ? String(overview.totalDecisions) : '—'}
          note={overview
            ? `${overview.processedToday} evaluated today (UTC) · includes deliberate re-imports`
            : 'Loading persisted evaluations'}
          icon={<Package />}
          actionLabel="Review decision history"
          onActivate={focusDecisionHistory}
        />
        <Metric
          label="Awaiting approval"
          value={overview ? String(overview.awaitingInsuranceApproval) : '—'}
          note="Insurance workflow holds"
          icon={<ShieldCheck />}
          actionLabel="Open insurance queue"
          onActivate={onNavigate.bind(null, 'insurance')}
        />
        <Metric
          label="Import issues today"
          value={overview ? String(overview.importIssues) : '—'}
          note="Rows requiring attention since 00:00 UTC"
          icon={<WarningCircle />}
          actionLabel="Review import issues"
          onActivate={onOpenImportAttention.bind(null, 'Issues')}
        />
        <Metric
          label="Batch queue"
          value={overview ? String(overview.pendingBatchRows) : '—'}
          note="Durable rows still pending"
          icon={<ClockCounterClockwise />}
          actionLabel="Open durable queue"
          onActivate={onOpenImportAttention.bind(null, 'Queue')}
        />
      </section>
      {error && <p className="notice notice-error"><WarningCircle aria-hidden />{error}</p>}

      <section className="panel queue-panel" id="decision-history">
        <div className="panel-heading history-heading">
          <div>
            <p className="section-kicker">Routing history</p>
            <h2>Decision history</h2>
            <p className="panel-description">Every successful evaluation is retained, including deliberate re-imports.</p>
          </div>
          <div className="history-controls">
            <TimeRangeSelector value={range} onChange={handleRangeChange} />
            <FilterSelect
              id="decision-history-filter"
              label="Filter decision history"
              value={decisionFilter}
              options={decisionFilterOptions}
              onChange={value =>
                handleDecisionFilterChange(value as RoutingDecisionFilter)}
            />
          </div>
        </div>
        <div className="history-summary">
          <span className="count-badge">
            {overview ? `${overview.decisionHistory.totalItems} decisions` : 'Loading'}
          </span>
          {range !== 'Recent' && overview && overview.decisionHistory.totalItems > 0 && (
            <span>Page {overview.decisionHistory.page} of {overview.decisionHistory.totalPages}</span>
          )}
        </div>
        {isLoading ? (
          <div className="activity-empty"><p>Loading decision history…</p></div>
        ) : decisions.length > 0 ? (
          <div className="table-scroll">
            <table>
              <thead><tr><th>Decision</th><th>Weight</th><th>Value (EUR)</th><th>Country</th><th>Department</th><th>Approval</th></tr></thead>
              <tbody>{decisions.map(renderDecision)}</tbody>
            </table>
          </div>
        ) : (
          <div className="empty-state">
            <span className="empty-icon"><Package aria-hidden /></span>
            <div>
              <strong>No decisions in this time range</strong>
              <p>Select another range, enter a parcel, or import an XML manifest.</p>
            </div>
            <button type="button" className="text-action" onClick={onNavigate.bind(null, 'parcel')}>
              Enter a parcel <ArrowRight aria-hidden />
            </button>
          </div>
        )}
        {range !== 'Recent' && overview && (
          <Pagination
            page={overview.decisionHistory.page}
            totalPages={overview.decisionHistory.totalPages}
            onChange={setPage}
          />
        )}
      </section>
    </div>
  )
}

/**
 * Renders the allow-listed history windows as one compact accessible control.
 */
function TimeRangeSelector({
  value,
  onChange,
  recentLabel = 'Recent 10',
}: {
  value: OperationsTimeRange
  onChange: (range: OperationsTimeRange) => void
  recentLabel?: string
}) {
  return (
    <div className="time-range-selector" role="group" aria-label="History time range">
      {historyRangeOptions.map(function renderTimeRange(option) {
        return (
          <button
            type="button"
            key={option.value}
            className={value === option.value ? 'is-active' : ''}
            aria-pressed={value === option.value}
            onClick={() => onChange(option.value)}
          >
            {option.value === 'Recent' ? recentLabel : option.label}
          </button>
        )
      })}
    </div>
  )
}

/**
 * Renders one labelled compact select for a server-owned history category.
 */
function FilterSelect({
  id,
  label,
  value,
  options,
  onChange,
}: {
  id: string
  label: string
  value: string
  options: Array<{ value: string; label: string }>
  onChange: (value: string) => void
}) {
  const [isOpen, setIsOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const selectedOption = options.find(option => option.value === value)

  useEffect(function closeFilterMenuOutsideItsBoundary() {
    if (!isOpen) return

    /**
     * Closes the filter listbox when the next pointer action is outside it.
     */
    function handlePointerDown(event: PointerEvent) {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) {
        setIsOpen(false)
      }
    }

    /**
     * Closes the listbox on Escape and returns keyboard focus to its trigger.
     */
    function handleEscape(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setIsOpen(false)
        triggerRef.current?.focus()
      }
    }

    document.addEventListener('pointerdown', handlePointerDown)
    document.addEventListener('keydown', handleEscape)
    return function removeFilterMenuListeners() {
      document.removeEventListener('pointerdown', handlePointerDown)
      document.removeEventListener('keydown', handleEscape)
    }
  }, [isOpen])

  /**
   * Applies one supported server filter and returns focus to its trigger so the
   * same interaction remains predictable for keyboard and pointer operators.
   */
  function selectFilter(nextValue: string) {
    onChange(nextValue)
    setIsOpen(false)
    triggerRef.current?.focus()
  }

  return (
    <div className="history-filter" ref={rootRef}>
      <button
        ref={triggerRef}
        type="button"
        className="history-filter-trigger"
        aria-label={label}
        aria-haspopup="listbox"
        aria-expanded={isOpen}
        aria-controls={`${id}-options`}
        onClick={() => setIsOpen(current => !current)}
      >
        <SlidersHorizontal aria-hidden />
        <span>{selectedOption?.label ?? label}</span>
        <CaretDown aria-hidden />
      </button>
      {isOpen && (
        <div
          className="history-filter-menu"
          id={`${id}-options`}
          role="listbox"
          aria-label={label}
        >
          {options.map(option => (
            <button
              type="button"
              role="option"
              aria-selected={option.value === value}
              className="history-filter-option"
              key={option.value}
              onClick={() => selectFilter(option.value)}
            >
              <span>{option.label}</span>
              {option.value === value && <CheckCircle aria-hidden />}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}

/**
 * Provides bounded page navigation without asking the browser to materialize
 * every historical record.
 */
function Pagination({
  page,
  totalPages,
  onChange,
}: {
  page: number
  totalPages: number
  onChange: (page: number) => void
}) {
  if (totalPages <= 1) return null

  const visiblePages = Array.from(
    new Set([1, page - 1, page, page + 1, totalPages]),
  )
    .filter(candidate => candidate >= 1 && candidate <= totalPages)
    .sort((first, second) => first - second)

  return (
    <nav className="pagination" aria-label="History pages">
      <button
        type="button"
        className="button button-secondary"
        disabled={page <= 1}
        onClick={() => onChange(page - 1)}
      >
        Previous
      </button>
      <div className="pagination-pages">
        {visiblePages.map(function renderPageButton(candidate, index) {
          const previous = visiblePages[index - 1]
          return (
            <Fragment key={candidate}>
              {previous && candidate - previous > 1 && <span aria-hidden>…</span>}
              <button
                type="button"
                className={candidate === page ? 'is-active' : ''}
                aria-current={candidate === page ? 'page' : undefined}
                aria-label={`Page ${candidate}`}
                onClick={() => onChange(candidate)}
              >
                {candidate}
              </button>
            </Fragment>
          )
        })}
      </div>
      <button
        type="button"
        className="button button-secondary"
        disabled={page >= totalPages}
        onClick={() => onChange(page + 1)}
      >
        Next
      </button>
    </nav>
  )
}

/**
 * Renders one measured status card.
 */
function Metric({
  label,
  value,
  note,
  icon,
  actionLabel,
  onActivate,
}: {
  label: string
  value: string
  note: string
  icon: ReactNode
  actionLabel?: string
  onActivate?: () => void
}) {
  const content = (
    <>
      <span className="metric-icon">{icon}</span>
      <span className="metric-label">{label}</span>
      <strong className="metric-value">{value}</strong>
      <small>{note}</small>
      {actionLabel && (
        <span className="metric-action">
          {actionLabel}
          <ArrowRight aria-hidden />
        </span>
      )}
    </>
  )

  return onActivate ? (
    <button type="button" className="metric is-actionable" onClick={onActivate}>
      {content}
    </button>
  ) : (
    <article className="metric">{content}</article>
  )
}

/**
 * Displays the four supported approval states with a consistent semantic badge.
 */
function ApprovalBadge({ decision }: { decision: RoutingDecision }) {
  const label = formatApproval(decision)
  const tone = decision.isInsuranceApproved
    ? 'approved'
    : decision.approvalState === 'PendingInsuranceApproval'
      ? 'waiting'
      : 'neutral'
  return <span className={`approval-badge ${tone}`}>{label}</span>
}

/**
 * Loads one persisted decision and its append-only approval evidence in a
 * shared detail drawer used by overview, imports, insurance, and activity.
 */
function DecisionDrawer({
  decisionId,
  identity,
  onClose,
  onOpenBatch,
  onApproved,
}: {
  decisionId: string
  identity: CurrentIdentity | null
  onClose: () => void
  onOpenBatch: (batchId: string) => void
  onApproved: () => void
}) {
  const [details, setDetails] = useState<RoutingDecisionDetails | null>(null)
  const [error, setError] = useState('')
  const [isApproving, setIsApproving] = useState(false)
  const canApprove = identity?.roles.includes('InsuranceApprover') ?? false

  useEffect(function loadDecisionDetails() {
    let isCurrent = true
    getDecision(decisionId)
      .then(function applyDetails(result) {
        if (isCurrent) setDetails(result)
      })
      .catch(function applyDetailError(reason: unknown) {
        if (isCurrent) setError(getErrorMessage(reason))
      })
    return function cancelDetailUpdate() {
      isCurrent = false
    }
  }, [decisionId])

  useEffect(function closeDrawerOnEscape() {
    /**
     * Gives every shared decision drawer the same keyboard dismissal behavior.
     */
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  /**
   * Appends approval evidence and reloads the complete server-owned detail so
   * every visible approval field changes together.
   */
  async function handleApprove() {
    if (!details) return
    setIsApproving(true)
    setError('')
    try {
      await approveInsurance(details.decision.id)
      setDetails(await getDecision(details.decision.id))
      onApproved()
    } catch (reason) {
      setError(getErrorMessage(reason))
    } finally {
      setIsApproving(false)
    }
  }

  const decision = details?.decision
  return (
    <div className="drawer-backdrop" role="presentation" onMouseDown={onClose}>
      <aside
        className="decision-drawer"
        role="dialog"
        aria-modal="true"
        aria-labelledby="decision-detail-title"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <div className="drawer-heading">
          <div>
            <p className="section-kicker">Routing decision</p>
            <h2 id="decision-detail-title">
              {decision ? `${decision.intendedDepartment} department` : 'Loading decision'}
            </h2>
          </div>
          <button type="button" className="button button-quiet" onClick={onClose} autoFocus>Close</button>
        </div>
        {error && <p className="notice notice-error"><WarningCircle aria-hidden />{error}</p>}
        {decision && (
          <>
            <div className="decision-state">
              <ApprovalBadge decision={decision} />
              <span>The department is intended; insurance approval releases the hold.</span>
            </div>
            <dl className="review-list detail-list">
              <div><dt>Weight</dt><dd>{decision.weightKilograms.toLocaleString()} kg</dd></div>
              <div><dt>Value (EUR)</dt><dd>€{decision.declaredValueEuros.toLocaleString(undefined, { minimumFractionDigits: 2 })}</dd></div>
              <div><dt>Destination</dt><dd>{formatCountry(decision.destinationCountry)}</dd></div>
              <div><dt>Rule set</dt><dd>Version {decision.ruleSetVersion}</dd></div>
              <div><dt>Evaluated</dt><dd>{formatTimestamp(decision.decidedAtUtc)}</dd></div>
            </dl>
            <section className="detail-section">
              <h3>Why this decision was made</h3>
              {decision.reasons.map(function renderReason(reason) {
                return <p className="reason-row" key={reason}><CheckCircle aria-hidden />{reason}</p>
              })}
            </section>
            {details.approval && (
              <section className="approval-evidence">
                <ShieldCheck aria-hidden />
                <div>
                  <strong>Insurance approval recorded</strong>
                  <p>{details.approval.approvedBy} · {formatTimestamp(details.approval.approvedAtUtc)}</p>
                </div>
              </section>
            )}
            {!details.approval
              && decision.approvalState === 'PendingInsuranceApproval'
              && canApprove && (
                <button
                  type="button"
                  className="button button-primary button-full"
                  onClick={handleApprove}
                  disabled={isApproving}
                >
                  <ShieldCheck aria-hidden />
                  {isApproving ? 'Recording approval…' : 'Approve insurance'}
                </button>
              )}
            {decision.batchId && (
              <button
                type="button"
                className="text-action"
                onClick={() => {
                  onOpenBatch(decision.batchId as string)
                  onClose()
                }}
              >
                Open related import <ArrowRight aria-hidden />
              </button>
            )}
            <details className="technical-details">
              <summary>Technical details</summary>
              <dl>
                <div><dt>Decision ID</dt><dd className="data-cell">{decision.id}</dd></div>
                <div><dt>Matched rule IDs</dt><dd className="data-cell">{decision.matchedRuleIds.join(', ')}</dd></div>
                <div><dt>Correlation ID</dt><dd className="data-cell">{decision.correlationId}</dd></div>
              </dl>
            </details>
          </>
        )}
      </aside>
    </div>
  )
}

/**
 * Submits one parcel to the real domain API and presents its explainable result.
 */
function NewParcel({ identity }: { identity: CurrentIdentity | null }) {
  const [draft, setDraft] = useState<ParcelDraft>(initialParcel)
  const [decision, setDecision] = useState<RoutingDecision | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [isApproving, setIsApproving] = useState(false)
  const [error, setError] = useState('')
  const canApprove = identity?.roles.includes('InsuranceApprover') ?? false

  const selectedCountry = useMemo(
    /**
     * Resolves the selected alpha-2 code to its registered English name.
     */
    function resolveCountryName() {
      return draft.country ? isoCountries.getName(draft.country, 'en') : undefined
    },
    [draft.country],
  )

  /**
   * Updates one parcel field and invalidates a stale decision.
   */
  function handleFieldChange(event: ChangeEvent<HTMLInputElement>) {
    const { name, value } = event.target
    setDraft(function updateDraft(current) {
      return { ...current, [name]: value }
    })
    setDecision(null)
    setError('')
  }

  /**
   * Stores one explicit ISO destination selected through the controlled country
   * picker and invalidates any decision based on the previous destination.
   */
  function handleDestinationCountry(countryCode: string) {
    setDraft(current => ({ ...current, country: countryCode }))
    setDecision(null)
    setError('')
  }

  /**
   * Routes the validated facts through the authenticated server boundary.
   */
  async function handleRoute(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!draft.country) {
      setError('Select a destination country before routing this parcel.')
      return
    }
    setIsSubmitting(true)
    setError('')
    try {
      const result = await routeParcel({
        weightKilograms: Number(draft.weight),
        declaredValueEuros: Number(draft.value),
        destinationCountry: draft.country,
        operatorReference: draft.reference.trim() || undefined,
      })
      setDecision(result.decision)
    } catch (reason) {
      setError(getErrorMessage(reason))
    } finally {
      setIsSubmitting(false)
    }
  }

  /**
   * Appends an insurance approval and updates only the visible workflow state.
   */
  async function handleApprove() {
    if (!decision) return
    setIsApproving(true)
    setError('')
    try {
      await approveInsurance(decision.id)
      setDecision({ ...decision, isInsuranceApproved: true })
    } catch (reason) {
      setError(getErrorMessage(reason))
    } finally {
      setIsApproving(false)
    }
  }

  /**
   * Clears all operator-entered values and server result state.
   */
  function handleReset() {
    setDraft(initialParcel)
    setDecision(null)
    setError('')
  }

  return (
    <div className="page">
      <section className="page-heading">
        <p className="eyebrow">Single entry</p>
        <h1>New parcel</h1>
        <p>Capture the facts required for a deterministic routing decision.</p>
      </section>
      {error && <p className="notice notice-error"><WarningCircle aria-hidden />{error}</p>}

      <div className="form-layout">
        <form className="panel entry-form" onSubmit={handleRoute}>
          <div className="panel-heading">
            <div><p className="section-kicker">Parcel details</p><h2>Routing inputs</h2></div>
            <span className="required-note">All fields except reference are required</span>
          </div>
          <div className="form-grid">
            <label className="field">
              <span>Weight</span>
              <span className="input-with-suffix">
                <Scales aria-hidden />
                <input name="weight" type="number" min="0.01" step="0.01" value={draft.weight} onChange={handleFieldChange} placeholder="0.00" required />
                <small>kg</small>
              </span>
            </label>
            <label className="field">
              <span>Declared value</span>
              <span className="input-with-suffix">
                <CurrencyEur aria-hidden />
                <input name="value" type="number" min="0" step="0.01" value={draft.value} onChange={handleFieldChange} placeholder="0.00" required />
                <small>EUR</small>
              </span>
            </label>
            <div className="field field-wide">
              <span id="destination-country-label">Destination country</span>
              <CountrySelect
                id="destination-country"
                labelId="destination-country-label"
                value={draft.country}
                placeholder="Select a country"
                onChange={handleDestinationCountry}
              />
            </div>
            <label className="field field-wide">
              <span>Operator reference <small>Optional</small></span>
              <span className="input-with-suffix">
                <FileText aria-hidden />
                <input name="reference" type="text" maxLength={60} value={draft.reference} onChange={handleFieldChange} placeholder="For example, BAY-04-017" />
              </span>
            </label>
          </div>
          <div className="form-actions">
            <button type="button" className="button button-quiet" onClick={handleReset}>Clear</button>
            <button type="submit" className="button button-primary" disabled={isSubmitting}>
              {isSubmitting ? 'Routing…' : 'Route parcel'} <ArrowRight aria-hidden />
            </button>
          </div>
        </form>

        <aside className="panel review-panel" aria-live="polite">
          <div>
            <p className="section-kicker">Server decision</p>
            <h2>{decision ? `${decision.intendedDepartment} department` : 'Awaiting parcel details'}</h2>
          </div>
          {decision ? (
            <>
              <dl className="review-list">
                <div><dt>Weight</dt><dd>{decision.weightKilograms.toLocaleString()} kg</dd></div>
                <div><dt>Value</dt><dd>€{decision.declaredValueEuros.toLocaleString(undefined, { minimumFractionDigits: 2 })}</dd></div>
                <div><dt>Destination</dt><dd>{selectedCountry}</dd></div>
                <div><dt>Rule set</dt><dd>Version {decision.ruleSetVersion}</dd></div>
                <div><dt>Matched rules</dt><dd>{decision.matchedRuleIds.join(', ')}</dd></div>
                <div><dt>Approval</dt><dd>{formatApproval(decision)}</dd></div>
              </dl>
              <div className="decision-reasons">
                {decision.reasons.map(function renderReason(reason) {
                  return <p key={reason}><CheckCircle aria-hidden />{reason}</p>
                })}
              </div>
              {canApprove && decision.approvalState === 'PendingInsuranceApproval' && !decision.isInsuranceApproved && (
                <button type="button" className="button button-primary button-full" onClick={handleApprove} disabled={isApproving}>
                  <ShieldCheck aria-hidden /> {isApproving ? 'Approving…' : 'Approve insurance'}
                </button>
              )}
            </>
          ) : (
            <div className="review-placeholder">
              <ListChecks aria-hidden />
              <p>Complete the form to receive an explainable persisted decision.</p>
            </div>
          )}
          <div className="boundary-callout">
            <LockKey aria-hidden />
            <p><strong>No route is calculated in the browser.</strong> The domain API owns routing and approval decisions.</p>
          </div>
        </aside>
      </div>
    </div>
  )
}

/**
 * Renders one bounded batch row for reconciliation and opens only its related
 * decision, keeping the history accordion responsible for batch selection.
 */
function renderBatchRow(
  row: BatchRow,
  onOpenDecision: (decisionId: string) => void,
) {
  const hasFailed = row.status === 'ValidationFailed'
    || row.status === 'ProcessingFailed'

  return (
    <tr key={row.id}>
      <td>{row.rowNumber}</td>
      <td>{row.weightKilograms.toLocaleString()} kg</td>
      <td>€{row.declaredValueEuros.toLocaleString()}</td>
      <td>{formatCountry(row.destinationCountry)}</td>
      <td>{row.decision?.intendedDepartment ?? '—'}</td>
      <td>{row.decision ? <ApprovalBadge decision={row.decision} /> : '—'}</td>
      <td>
        <span className={hasFailed ? 'error-badge' : 'active-badge'}>
          {formatBatchRowStatus(row.status)}
        </span>
      </td>
      <td>
        {row.decision ? (
          <button
            type="button"
            className="text-action compact-action"
            onClick={() => onOpenDecision(row.decision?.id ?? '')}
          >
            View
          </button>
        ) : hasFailed ? (
          <details className="row-issue-details">
            <summary>Review issue</summary>
            <strong>{formatOperatorIssue(row.errorMessage)}</strong>
            <span>{getBatchRowRecovery(row.status)}</span>
            <code>{row.status} · {row.errorCode ?? 'routing.batch.row_failed'}</code>
          </details>
        ) : null}
      </td>
    </tr>
  )
}

/**
 * Displays one persisted batch directly beneath its selected history row so
 * operators retain context and can collapse or switch batches predictably.
 */
function BatchDetails({
  batch,
  onOpenDecision,
}: {
  batch: Batch
  onOpenDecision: (decisionId: string) => void
}) {
  const awaitingApproval = batch.rows.filter(row =>
    row.decision?.approvalState === 'PendingInsuranceApproval'
    && !row.decision.isInsuranceApproved).length

  return (
    <section
      className="batch-details-inline"
      id={`batch-details-${batch.id}`}
      aria-live="polite"
    >
      <div className="panel-heading">
        <div>
          <p className="section-kicker">Batch {batch.id.slice(0, 8)}</p>
          <h2>{formatBatchStatus(batch.status)}</h2>
        </div>
        <span className="count-badge">
          {batch.completedRows + batch.failedRows} / {batch.totalRows} rows
        </span>
      </div>
      <p className="batch-summary">
        <strong>{batch.completedRows} evaluated</strong>
        <span>·</span>
        <strong>{awaitingApproval} awaiting insurance approval</strong>
        <span>·</span>
        <strong>{batch.failedRows} failed</strong>
      </p>
      <p className="processing-explainer">
        Evaluated means validation, decision creation, and persistence succeeded.
        It does not mean dispatched, delivered, or insurance-approved.
      </p>
      {batch.failedRows > 0 && (
        <p className="correction-guidance">
          <WarningCircle aria-hidden />
          <span>
            <strong>Failed rows are not edited in this historical batch.</strong>
            Correct the source XML and import a correction manifest. Importing a
            corrected full manifest evaluates every row again; importing only
            corrected rows avoids repeating successful rows.
          </span>
        </p>
      )}
      <div
        className="progress-track"
        aria-label={`${batch.completedRows + batch.failedRows} of ${batch.totalRows} rows finished`}
      >
        <span
          style={{
            transform: `scaleX(${batch.totalRows
              ? (batch.completedRows + batch.failedRows) / batch.totalRows
              : 0})`,
          }}
        />
      </div>
      <div className="table-scroll batch-row-table">
        <table>
          <thead>
            <tr>
              <th>Row</th>
              <th>Weight</th>
              <th>Value (EUR)</th>
              <th>Country</th>
              <th>Department</th>
              <th>Approval</th>
              <th>Processing</th>
              <th>Details</th>
            </tr>
          </thead>
          <tbody>{batch.rows.map(row => renderBatchRow(row, onOpenDecision))}</tbody>
        </table>
      </div>
    </section>
  )
}

/**
 * Shows the concrete privacy-safe rows represented by the Overview import
 * issues and batch queue KPIs.
 */
function ImportAttentionPanel({
  selectedKind,
  onOpenBatch,
}: {
  selectedKind: ImportAttentionKind | null
  onOpenBatch: (batchId: string) => Promise<void>
}) {
  const [kind, setKind] = useState<ImportAttentionKind>(
    selectedKind ?? 'Issues',
  )
  const [attention, setAttention] =
    useState<PagedResponse<ImportAttentionItem> | null>(null)
  const [page, setPage] = useState(1)
  const [error, setError] = useState('')
  const [isLoading, setIsLoading] = useState(true)
  const panelRef = useRef<HTMLElement>(null)
  const items = attention?.items ?? []

  useEffect(function applyRequestedAttentionKind() {
    if (!selectedKind) return
    setKind(selectedKind)
    setPage(1)
    window.requestAnimationFrame(() => {
      panelRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    })
  }, [selectedKind])

  useEffect(
    /**
     * Retrieves one bounded server-filtered attention page and ignores stale
     * responses after the operator changes category or page.
     */
    function loadImportAttention() {
      let isCurrent = true
      setIsLoading(true)
      setError('')
      getImportAttention(kind, page)
        .then(function applyImportAttention(result) {
          if (isCurrent) setAttention(result)
        })
        .catch(function applyImportAttentionError(reason: unknown) {
          if (isCurrent) setError(getErrorMessage(reason))
        })
        .finally(function finishImportAttentionLoad() {
          if (isCurrent) setIsLoading(false)
        })
      return function cancelImportAttentionUpdate() {
        isCurrent = false
      }
    },
    [kind, page],
  )

  /**
   * Changes between permanent issues and transient queue work without carrying
   * a page number from the previous result set.
   */
  function handleKindChange(nextKind: ImportAttentionKind) {
    setKind(nextKind)
    setPage(1)
  }

  /**
   * Gives an operator-friendly explanation while preserving stable technical
   * codes as secondary support information.
   */
  function getAttentionExplanation(item: ImportAttentionItem) {
    if (kind === 'Queue') {
      return item.status === 'Processing'
        ? `Processing attempt ${Math.max(item.attemptCount, 1)} is in progress.`
        : 'Waiting for the durable batch processor.'
    }
    return formatOperatorIssue(item.errorMessage)
  }

  return (
    <section className="panel attention-panel" id="import-attention" ref={panelRef}>
      <div className="panel-heading history-heading">
        <div>
          <p className="section-kicker">Operational follow-up</p>
          <h2>Import attention</h2>
          <p className="panel-description">
            Inspect failed rows or current durable work without exposing raw XML.
            Historical batches remain unchanged after corrections.
          </p>
        </div>
        <div className="attention-kind-selector" role="group" aria-label="Import attention type">
          <button
            type="button"
            className={kind === 'Issues' ? 'is-active' : ''}
            aria-pressed={kind === 'Issues'}
            onClick={() => handleKindChange('Issues')}
          >
            Import issues today
          </button>
          <button
            type="button"
            className={kind === 'Queue' ? 'is-active' : ''}
            aria-pressed={kind === 'Queue'}
            onClick={() => handleKindChange('Queue')}
          >
            Batch queue
          </button>
        </div>
      </div>
      <div className="history-summary">
        <span className="count-badge">
          {attention
            ? `${attention.totalItems} ${kind === 'Issues' ? 'issues' : 'rows'}`
            : 'Loading'}
        </span>
        {attention && attention.totalItems > 0 && (
          <span>Page {attention.page} of {attention.totalPages}</span>
        )}
      </div>
      <p className="attention-guidance">
        {kind === 'Issues'
          ? <>
              <strong>Needs correction</strong> means the source row is invalid.
              <strong>Processing failed</strong> means a valid row reached durable
              processing but could not finish safely.
            </>
          : <>
              <strong>Waiting</strong> and <strong>Processing</strong> are temporary
              states that leave this queue after evaluation finishes.
            </>}
      </p>
      {error && <p className="notice notice-error"><WarningCircle aria-hidden />{error}</p>}
      {isLoading ? (
        <div className="activity-empty"><p>Loading import attention…</p></div>
      ) : items.length > 0 ? (
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Batch</th>
                <th>Row</th>
                <th>Imported</th>
                <th>State</th>
                <th>{kind === 'Issues' ? 'Issue' : 'Queue details'}</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              {items.map(item => (
                <tr key={item.rowId}>
                  <td className="data-cell">{item.batchId.slice(0, 8)}</td>
                  <td>{item.rowNumber}</td>
                  <td>{formatTimestamp(item.batchCreatedAtUtc)}</td>
                  <td>
                    <span className={kind === 'Issues'
                      ? 'attention-state is-issue'
                      : 'attention-state is-queue'}
                    >
                      {formatBatchRowStatus(item.status)}
                    </span>
                  </td>
                  <td>
                    <strong className="attention-message">
                      {getAttentionExplanation(item)}
                    </strong>
                    {kind === 'Issues' && (
                      <span className="attention-recovery">
                        {getBatchRowRecovery(item.status)}
                      </span>
                    )}
                    {item.errorCode && (
                      <span className="technical-secondary">
                        {item.status} · {item.errorCode}
                      </span>
                    )}
                  </td>
                  <td>
                    <button
                      type="button"
                      className="text-action"
                      onClick={() => void onOpenBatch(item.batchId)}
                    >
                      Review batch <ArrowRight aria-hidden />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <div className="empty-state attention-empty">
          <span className="empty-icon">
            {kind === 'Issues'
              ? <CheckCircle aria-hidden />
              : <ClockCounterClockwise aria-hidden />}
          </span>
          <div>
            <strong>
              {kind === 'Issues'
                ? 'No import issues today'
                : 'The durable batch queue is clear'}
            </strong>
            <p>
              {kind === 'Issues'
                ? 'Rows that fail validation or permanent processing will appear here.'
                : 'Pending or processing rows will appear here while work is active.'}
            </p>
          </div>
        </div>
      )}
      {attention && (
        <Pagination
          page={attention.page}
          totalPages={attention.totalPages}
          onChange={setPage}
        />
      )}
    </section>
  )
}

/**
 * Uploads a bounded XML manifest and polls its independently durable row states.
 */
function ImportManifest({
  selectedBatchId,
  selectedAttentionKind,
  onOpenDecision,
}: {
  selectedBatchId: string | null
  selectedAttentionKind: ImportAttentionKind | null
  onOpenDecision: (decisionId: string) => void
}) {
  const [workspaceMode, setWorkspaceMode] = useState<ImportWorkspaceMode>(
    selectedBatchId || selectedAttentionKind ? 'operations' : 'new',
  )
  const [file, setFile] = useState<File | null>(null)
  const [country, setCountry] = useState('')
  const [batch, setBatch] = useState<Batch | null>(null)
  const [latestCreatedBatchId, setLatestCreatedBatchId] = useState<string | null>(null)
  const [recentBatches, setRecentBatches] = useState<BatchSummary[]>([])
  const [duplicate, setDuplicate] = useState<{
    batchId: string
    importedAtUtc: string
  } | null>(null)
  const [fileError, setFileError] = useState('')
  const [requestError, setRequestError] = useState('')
  const [isImporting, setIsImporting] = useState(false)
  const batchSelectionRequest = useRef(0)
  const pendingBatchScroll = useRef<string | null>(selectedBatchId)

  useEffect(function applyRequestedImportWorkspace() {
    if (selectedBatchId || selectedAttentionKind) {
      setWorkspaceMode('operations')
    }
    if (selectedBatchId) {
      pendingBatchScroll.current = selectedBatchId
    }
  }, [selectedAttentionKind, selectedBatchId])

  useEffect(function loadImportHistory() {
    let isCurrent = true
    getRecentBatches()
      .then(function applyHistory(result) {
        if (isCurrent) setRecentBatches(result)
      })
      .catch(function applyHistoryError(reason: unknown) {
        if (isCurrent) setRequestError(getErrorMessage(reason))
      })
    return function cancelHistoryUpdate() {
      isCurrent = false
    }
  }, [])

  useEffect(function restoreSelectedBatch() {
    if (!selectedBatchId) return
    let isCurrent = true
    getBatch(selectedBatchId)
      .then(function applyRestoredBatch(result) {
        if (isCurrent) setBatch(result)
      })
      .catch(function applyRestoreError(reason: unknown) {
        if (isCurrent) setRequestError(getErrorMessage(reason))
      })
    return function cancelRestoreUpdate() {
      isCurrent = false
    }
  }, [selectedBatchId])

  useEffect(
    /**
     * Waits for both the operations panel and selected batch detail to render
     * before aligning the exact batch beneath the sticky workspace header.
     */
    function alignRequestedBatchDetail() {
      const requestedBatchId = pendingBatchScroll.current
      if (workspaceMode !== 'operations'
        || !requestedBatchId
        || batch?.id !== requestedBatchId) {
        return
      }

      let detailFrame = 0
      const renderFrame = window.requestAnimationFrame(() => {
        detailFrame = window.requestAnimationFrame(() => {
          document.getElementById(`batch-details-${requestedBatchId}`)
            ?.scrollIntoView({ behavior: 'smooth', block: 'start' })
          pendingBatchScroll.current = null
        })
      })
      return function cancelBatchAlignment() {
        window.cancelAnimationFrame(renderFrame)
        window.cancelAnimationFrame(detailFrame)
      }
    },
    [batch, workspaceMode],
  )

  /**
   * Expands one persisted batch, switches to another, or collapses the current
   * selection. A sequence token prevents a slower previous request from
   * replacing a newer operator choice.
   */
  async function toggleBatch(batchId: string) {
    if (batch?.id === batchId) {
      batchSelectionRequest.current += 1
      setBatch(null)
      return
    }

    const requestSequence = ++batchSelectionRequest.current
    setRequestError('')
    try {
      const selected = await getBatch(batchId)
      if (requestSequence === batchSelectionRequest.current) {
        setBatch(selected)
      }
    } catch (reason) {
      if (requestSequence === batchSelectionRequest.current) {
        setRequestError(getErrorMessage(reason))
      }
    }
  }

  /**
   * Opens a batch selected from the attention read model and then moves the
   * viewport to its restored durable detail without toggling an open batch shut.
   */
  async function openBatchInOperations(batchId: string) {
    pendingBatchScroll.current = batchId
    setWorkspaceMode('operations')
    if (batch?.id !== batchId) {
      await toggleBatch(batchId)
    }
  }

  useEffect(
    /**
     * Polls only non-terminal batches and cancels the timer on navigation.
     */
    function pollBatch() {
      if (!batch || batch.status === 'Completed' || batch.status === 'CompletedWithErrors') {
        return
      }
      let isCurrent = true
      const timer = window.setTimeout(function refreshBatch() {
        getBatch(batch.id)
          .then(function applyBatch(result) {
            if (isCurrent) {
              setBatch(result)
              if (result.status === 'Completed'
                || result.status === 'CompletedWithErrors') {
                getRecentBatches().then(setRecentBatches).catch(() => undefined)
              }
            }
          })
          .catch(function applyPollingError(reason: unknown) {
            if (isCurrent) setRequestError(getErrorMessage(reason))
          })
      }, 750)
      return function cancelBatchPolling() {
        isCurrent = false
        window.clearTimeout(timer)
      }
    },
    [batch],
  )

  /**
   * Accepts a single XML file under the same two-megabyte HTTP boundary.
   */
  function handleFileChange(event: ChangeEvent<HTMLInputElement>) {
    const selected = event.target.files?.[0] ?? null
    setBatch(null)
    setLatestCreatedBatchId(null)
    setDuplicate(null)
    setRequestError('')
    if (!selected) {
      setFile(null)
      setFileError('')
      return
    }
    if (!selected.name.toLowerCase().endsWith('.xml')) {
      setFile(null)
      setFileError('Choose an XML file. Other file types are not accepted.')
      event.target.value = ''
      return
    }
    if (selected.size > 2_097_152) {
      setFile(null)
      setFileError('The XML file must not exceed 2 MB.')
      event.target.value = ''
      return
    }
    setFile(selected)
    setFileError('')
  }

  /**
   * Stores the explicit fallback country used only by rows missing one.
   */
  function handleCountryChange(countryCode: string) {
    setCountry(countryCode)
    setRequestError('')
  }

  /**
   * Sends the raw XML stream and begins durable progress observation.
   */
  async function handleImport(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!file) {
      setFileError('Choose an XML manifest before importing.')
      return
    }
    if (!country) {
      setRequestError('Select a fallback country before importing this manifest.')
      return
    }
    await submitImport(false)
  }

  /**
   * Imports once or deliberately confirms a previously detected manifest while
   * preserving every source row inside the file.
   */
  async function submitImport(confirmDuplicate: boolean) {
    if (!file) return
    setIsImporting(true)
    setRequestError('')
    if (!confirmDuplicate) setDuplicate(null)
    try {
      const imported = await importXmlManifest(file, country, confirmDuplicate)
      setWorkspaceMode('new')
      setBatch(imported)
      setLatestCreatedBatchId(imported.id)
      setDuplicate(null)
      setRecentBatches(await getRecentBatches())
    } catch (reason) {
      if (reason instanceof ApiError
        && reason.errorCode === 'routing.batch.duplicate_manifest'
        && reason.previousBatchId
        && reason.previousImportedAtUtc) {
        setDuplicate({
          batchId: reason.previousBatchId,
          importedAtUtc: reason.previousImportedAtUtc,
        })
      } else {
        setRequestError(getErrorMessage(reason))
      }
    } finally {
      setIsImporting(false)
    }
  }

  return (
    <div className="page">
      <section className="page-heading">
        <p className="eyebrow">Legacy manifest</p>
        <h1>Import XML</h1>
        <p>Validate the selected manifest and complete missing destination context.</p>
      </section>
      {requestError && <p className="notice notice-error"><WarningCircle aria-hidden />{requestError}</p>}
      {duplicate && (
        <section className="notice duplicate-warning" aria-live="polite">
          <WarningCircle aria-hidden />
          <div>
            <strong>This manifest and fallback country were imported before.</strong>
            <p>Previous batch {duplicate.batchId.slice(0, 8)} · {formatTimestamp(duplicate.importedAtUtc)}. Valid duplicate rows inside the file will still be preserved.</p>
            <div className="inline-actions">
              <button type="button" className="button button-secondary" onClick={() => void openBatchInOperations(duplicate.batchId)}>
                Review previous batch
              </button>
              <button type="button" className="button button-primary" onClick={() => submitImport(true)} disabled={isImporting}>
                Import again
              </button>
            </div>
          </div>
        </section>
      )}

      <div className="import-workspace-tabs" role="tablist" aria-label="Import XML workspace">
        <button
          id="import-new-tab"
          type="button"
          role="tab"
          aria-selected={workspaceMode === 'new'}
          aria-controls="import-new-panel"
          className={workspaceMode === 'new' ? 'is-active' : ''}
          onClick={() => setWorkspaceMode('new')}
        >
          <FileArrowUp aria-hidden />
          <span><strong>New import</strong><small>Upload and evaluate XML</small></span>
        </button>
        <button
          id="import-operations-tab"
          type="button"
          role="tab"
          aria-selected={workspaceMode === 'operations'}
          aria-controls="import-operations-panel"
          className={workspaceMode === 'operations' ? 'is-active' : ''}
          onClick={() => setWorkspaceMode('operations')}
        >
          <ListChecks aria-hidden />
          <span><strong>Operations &amp; history</strong><small>Resolve issues and review batches</small></span>
        </button>
      </div>

      {workspaceMode === 'new' ? (
        <div
          id="import-new-panel"
          role="tabpanel"
          aria-labelledby="import-new-tab"
        >
          <div className="import-layout">
            <form className="panel import-panel" onSubmit={handleImport}>
              <div className="panel-heading">
                <div><p className="section-kicker">Source file</p><h2>Manifest setup</h2></div>
                <span className="format-badge">XML only</span>
              </div>
              <label className={fileError ? 'dropzone has-error' : 'dropzone'}>
                <input type="file" accept=".xml,text/xml,application/xml" onChange={handleFileChange} />
                <span className="dropzone-icon"><FileArrowUp aria-hidden /></span>
                <strong>{file?.name ?? 'Choose an XML manifest'}</strong>
                <span>{file ? `${file.size.toLocaleString()} bytes selected` : 'Select the assignment file from this device'}</span>
              </label>
              {fileError && <p className="field-error"><WarningCircle aria-hidden />{fileError}</p>}
              <div className="field">
                <span id="fallback-country-label">Country for parcels missing a country</span>
                <CountrySelect
                  id="fallback-country"
                  labelId="fallback-country-label"
                  value={country}
                  placeholder="Select the fallback country"
                  onChange={handleCountryChange}
                />
              </div>
              <div className="form-actions">
                <span className="connection-note"><Info aria-hidden /> Raw XML is validated server-side</span>
                <button type="submit" className="button button-primary" disabled={isImporting || !file || !country}>
                  {isImporting ? 'Validating…' : 'Import manifest'} <FileArrowUp aria-hidden />
                </button>
              </div>
            </form>

            <aside className="panel safeguard-panel">
              <p className="section-kicker">Enforced safeguards</p>
              <h2>How import fails safely</h2>
              <ul className="safeguard-list">
                <li><ShieldCheck aria-hidden /><span><strong>Secure XML parsing</strong>External entities and document type declarations are prohibited</span></li>
                <li><ListChecks aria-hidden /><span><strong>Durable row processing</strong>Each parcel completes or fails independently with bounded retries</span></li>
                <li><MapPin aria-hidden /><span><strong>Country provenance</strong>Every row records XML or fallback as its country source</span></li>
              </ul>
            </aside>
          </div>

          {batch?.id === latestCreatedBatchId && (
            <section className="panel selected-import-panel">
              <div className="panel-heading">
                <div>
                  <p className="section-kicker">Latest import result</p>
                  <h2>Batch {batch.id.slice(0, 8)}</h2>
                </div>
                <button
                  type="button"
                  className="text-action"
                  onClick={() => setWorkspaceMode('operations')}
                >
                  Open operations <ArrowRight aria-hidden />
                </button>
              </div>
              <BatchDetails batch={batch} onOpenDecision={onOpenDecision} />
            </section>
          )}
        </div>
      ) : (
        <div
          id="import-operations-panel"
          role="tabpanel"
          aria-labelledby="import-operations-tab"
        >
          <ImportAttentionPanel
            selectedKind={selectedAttentionKind}
            onOpenBatch={openBatchInOperations}
          />

          {batch && !recentBatches.some(item => item.id === batch.id) && (
            <section className="panel selected-import-panel" id="selected-import-details">
              <div className="panel-heading">
                <div>
                  <p className="section-kicker">Selected durable import</p>
                  <h2>Batch {batch.id.slice(0, 8)}</h2>
                </div>
                <button type="button" className="text-action" onClick={() => setBatch(null)}>
                  Close
                </button>
              </div>
              <BatchDetails batch={batch} onOpenDecision={onOpenDecision} />
            </section>
          )}

          <section className="panel table-panel import-history" id="recent-imports">
            <div className="panel-heading">
              <div><p className="section-kicker">Durable history</p><h2>Recent imports</h2></div>
              <span className="count-badge">{recentBatches.length} batches</span>
            </div>
            {recentBatches.length > 0 ? (
              <div className="table-scroll">
                <table>
                  <thead><tr><th>Batch</th><th>Imported</th><th>Fallback country</th><th>Evaluated</th><th>Failures</th><th>Awaiting approval</th><th>Status</th></tr></thead>
                  <tbody>
                    {recentBatches.map(function renderBatchSummary(item) {
                      const isExpanded = batch?.id === item.id
                      return (
                        <Fragment key={item.id}>
                          <tr
                            id={`batch-summary-${item.id}`}
                            className={isExpanded ? 'clickable-row is-expanded' : 'clickable-row'}
                            aria-label={`${isExpanded ? 'Collapse' : 'Expand'} import batch ${item.id.slice(0, 8)}`}
                            aria-expanded={isExpanded}
                            aria-controls={`batch-details-${item.id}`}
                            onClick={() => toggleBatch(item.id)}
                            tabIndex={0}
                            onKeyDown={event => {
                              if (event.key === 'Enter' || event.key === ' ') {
                                event.preventDefault()
                                toggleBatch(item.id)
                              }
                            }}
                          >
                            <td>
                              <span className="batch-history-id data-cell">
                                <CaretDown aria-hidden />
                                {item.id.slice(0, 8)}
                              </span>
                            </td>
                            <td>{formatTimestamp(item.createdAtUtc)}</td>
                            <td>{item.fallbackDestinationCountry ? formatCountry(item.fallbackDestinationCountry) : 'Per row'}</td>
                            <td>{item.completedRows} / {item.totalRows}</td>
                            <td>{item.failedRows}</td>
                            <td>{item.awaitingInsuranceApproval}</td>
                            <td>{formatBatchStatus(item.status)}</td>
                          </tr>
                          {isExpanded && batch && (
                            <tr className="history-expansion-row">
                              <td colSpan={7}>
                                <BatchDetails
                                  batch={batch}
                                  onOpenDecision={onOpenDecision}
                                />
                              </td>
                            </tr>
                          )}
                        </Fragment>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            ) : (
              <div className="activity-empty"><p>No imports recorded yet.</p></div>
            )}
          </section>
        </div>
      )}
    </div>
  )
}

/**
 * Loads unresolved high-value decisions into a role-aware insurance work queue.
 */
function InsuranceQueue({
  identity,
  onOpenDecision,
}: {
  identity: CurrentIdentity | null
  onOpenDecision: (decisionId: string) => void
}) {
  const [queue, setQueue] = useState<PagedResponse<RoutingDecision> | null>(null)
  const [page, setPage] = useState(1)
  const [error, setError] = useState('')
  const [isLoading, setIsLoading] = useState(true)
  const canApprove = identity?.roles.includes('InsuranceApprover') ?? false
  const decisions = queue?.items ?? []

  useEffect(function loadApprovalQueue() {
    let isCurrent = true
    setIsLoading(true)
    setError('')
    getAwaitingInsurance(page)
      .then(function applyQueue(result) {
        if (isCurrent) setQueue(result)
      })
      .catch(function applyQueueError(reason: unknown) {
        if (isCurrent) setError(getErrorMessage(reason))
      })
      .finally(function finishQueueLoad() {
        if (isCurrent) setIsLoading(false)
      })
    return function cancelQueueUpdate() {
      isCurrent = false
    }
  }, [page])

  return (
    <div className="page">
      <section className="page-heading heading-with-actions">
        <div>
          <p className="eyebrow">Insurance workflow</p>
          <h1>Awaiting insurance</h1>
          <p>Review high-value parcels before their intended department may physically route them.</p>
        </div>
        <span className={canApprove ? 'active-badge' : 'readonly-badge'}>
          {canApprove ? 'Approval access' : 'View only'}
        </span>
      </section>
      {error && <p className="notice notice-error"><WarningCircle aria-hidden />{error}</p>}
      <section className="panel table-panel">
        <div className="panel-heading">
          <div><p className="section-kicker">Oldest first</p><h2>Approval work queue</h2></div>
          <span className="count-badge">{queue ? `${queue.totalItems} awaiting` : 'Loading'}</span>
        </div>
        {isLoading ? (
          <div className="activity-empty"><p>Loading approval work…</p></div>
        ) : decisions.length > 0 ? (
          <div className="table-scroll">
            <table>
              <thead><tr><th>Decision</th><th>Created</th><th>Value (EUR)</th><th>Country</th><th>Intended department</th><th>Status</th></tr></thead>
              <tbody>
                {decisions.map(function renderApprovalRow(decision) {
                  return (
                    <tr
                      key={decision.id}
                      className="clickable-row"
                      aria-label={`Open insurance decision ${decision.id.slice(0, 8)}`}
                      onClick={() => onOpenDecision(decision.id)}
                      tabIndex={0}
                      onKeyDown={event => {
                        if (event.key === 'Enter' || event.key === ' ') {
                          event.preventDefault()
                          onOpenDecision(decision.id)
                        }
                      }}
                    >
                      <td className="data-cell">{decision.id.slice(0, 8)}</td>
                      <td>{formatTimestamp(decision.decidedAtUtc)}</td>
                      <td>€{decision.declaredValueEuros.toLocaleString(undefined, { minimumFractionDigits: 2 })}</td>
                      <td>{formatCountry(decision.destinationCountry)}</td>
                      <td><strong>{decision.intendedDepartment}</strong></td>
                      <td><ApprovalBadge decision={decision} /></td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="activity-empty">
            <ShieldCheck className="activity-icon" aria-hidden />
            <h2>No insurance holds are waiting</h2>
            <p>Approved and not-required decisions do not appear in this queue.</p>
          </div>
        )}
        {queue && (
          <Pagination
            page={queue.page}
            totalPages={queue.totalPages}
            onChange={setPage}
          />
        )}
      </section>
    </div>
  )
}

/**
 * Loads the constrained rule lifecycle and exposes typed administration only
 * to identities carrying the server-recognized RuleAdministrator role.
 */
function Rules({
  identity,
  selectedVersion,
}: {
  identity: CurrentIdentity | null
  selectedVersion: number | null
}) {
  const [ruleSet, setRuleSet] = useState<ActiveRuleSet | null>(null)
  const [versions, setVersions] = useState<ActiveRuleSet[]>([])
  const [draft, setDraft] = useState({
    mailUpperKilograms: '1',
    regularUpperKilograms: '10',
    insuranceThresholdEuros: '1000',
  })
  const [candidate, setCandidate] = useState<ActiveRuleSet | null>(null)
  const [sourceVersion, setSourceVersion] = useState<number | null>(null)
  const [highlightedVersion, setHighlightedVersion] = useState<number | null>(null)
  const [simulation, setSimulation] = useState<RuleSimulation | null>(null)
  const [error, setError] = useState('')
  const [isWorking, setIsWorking] = useState(false)
  const canAdminister = identity?.roles.includes('RuleAdministrator') ?? false

  useEffect(
    /**
     * Retrieves the immutable active rule set when the view opens.
     */
    function loadRules() {
      let isCurrent = true
      Promise.all([getActiveRules(), getRuleVersions()])
        .then(function applyRules([active, history]) {
          if (isCurrent) {
            setRuleSet(active)
            setVersions(history)
            const latestDraft = selectedVersion === null
              ? history.find(version => version.status === 'Draft')
              : undefined
            if (latestDraft) {
              resumeDraft(latestDraft)
            }
          }
        })
        .catch(function applyRulesError(reason: unknown) {
          if (isCurrent) setError(getErrorMessage(reason))
        })
      return function cancelRulesUpdate() {
        isCurrent = false
      }
    },
    [selectedVersion],
  )

  useEffect(
    /**
     * Restores the exact rule version selected from Activity and scrolls its
     * durable history row into view. Draft events also reopen their candidate.
     */
    function restoreSelectedRuleVersion() {
      if (selectedVersion === null || versions.length === 0) return
      const selected = versions.find(version => version.version === selectedVersion)
      if (!selected) return
      setHighlightedVersion(selected.version)
      if (selected.status === 'Draft') {
        resumeDraft(selected)
      }
      const animationFrame = window.requestAnimationFrame(function revealVersionRow() {
        document.getElementById(`rule-version-${selected.version}`)?.scrollIntoView({
          behavior: 'smooth',
          block: 'center',
        })
      })
      return function cancelVersionReveal() {
        window.cancelAnimationFrame(animationFrame)
      }
    },
    [selectedVersion, versions],
  )

  /**
   * Converts one server-owned typed version into editor strings without
   * reverse-parsing its human-readable rule descriptions.
   */
  function getDraftValues(version: ActiveRuleSet) {
    return {
      mailUpperKilograms: version.mailUpperKilograms.toString(),
      regularUpperKilograms: version.regularUpperKilograms.toString(),
      insuranceThresholdEuros: version.insuranceThresholdEuros.toString(),
    }
  }

  /**
   * Restores an immutable Draft as the current simulation and activation
   * candidate. Editing any restored value deliberately branches to a new
   * version rather than rewriting audited history.
   */
  function resumeDraft(version: ActiveRuleSet) {
    setDraft(getDraftValues(version))
    setCandidate(version)
    setSourceVersion(version.version)
    setHighlightedVersion(version.version)
    setSimulation(null)
  }

  /**
   * Copies an active or retired definition into the editor as the starting
   * point for a new immutable version.
   */
  function copyVersionToNewDraft(version: ActiveRuleSet) {
    setDraft(getDraftValues(version))
    setCandidate(null)
    setSourceVersion(version.version)
    setHighlightedVersion(version.version)
    setSimulation(null)
  }

  /**
   * Reloads active policy and bounded history after a lifecycle transition.
   */
  async function refreshRules() {
    const [active, history] = await Promise.all([
      getActiveRules(),
      getRuleVersions(),
    ])
    setRuleSet(active)
    setVersions(history)
  }

  /**
   * Updates one constrained numeric boundary and invalidates stale simulation.
   */
  function handleDraftChange(event: ChangeEvent<HTMLInputElement>) {
    const { name, value } = event.target
    setDraft(current => ({ ...current, [name]: value }))
    setCandidate(null)
    setSimulation(null)
  }

  /**
   * Stores a domain-validated immutable draft using the next version number.
   */
  async function handleCreateDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setIsWorking(true)
    setError('')
    try {
      const nextVersion = Math.max(0, ...versions.map(version => version.version)) + 1
      const created = await createRuleDraft({
        version: nextVersion,
        mailUpperKilograms: Number(draft.mailUpperKilograms),
        regularUpperKilograms: Number(draft.regularUpperKilograms),
        insuranceThresholdEuros: Number(draft.insuranceThresholdEuros),
      })
      setCandidate(created)
      setSourceVersion(created.version)
      setHighlightedVersion(created.version)
      setSimulation(null)
      await refreshRules()
    } catch (reason) {
      setError(getErrorMessage(reason))
    } finally {
      setIsWorking(false)
    }
  }

  /**
   * Compares boundary-focused representative parcels before activation.
   */
  async function handleSimulate() {
    if (!candidate) return
    setIsWorking(true)
    setError('')
    try {
      const mail = Number(draft.mailUpperKilograms)
      const regular = Number(draft.regularUpperKilograms)
      const insurance = Number(draft.insuranceThresholdEuros)
      setSimulation(await simulateRuleSet(candidate.version, [
        { sampleId: 'mail-boundary', weightKilograms: mail, declaredValueEuros: 100, destinationCountry: 'GB' },
        { sampleId: 'above-mail', weightKilograms: mail + 0.01, declaredValueEuros: 100, destinationCountry: 'GB' },
        { sampleId: 'regular-boundary', weightKilograms: regular, declaredValueEuros: 100, destinationCountry: 'GB' },
        { sampleId: 'above-regular', weightKilograms: regular + 0.01, declaredValueEuros: 100, destinationCountry: 'GB' },
        { sampleId: 'insurance-boundary', weightKilograms: 0.5, declaredValueEuros: insurance, destinationCountry: 'GB' },
        { sampleId: 'above-insurance', weightKilograms: 0.5, declaredValueEuros: insurance + 0.01, destinationCountry: 'GB' },
      ]))
    } catch (reason) {
      setError(getErrorMessage(reason))
    } finally {
      setIsWorking(false)
    }
  }

  /**
   * Activates only a candidate that has completed the visible simulation step.
   */
  async function handleActivate() {
    if (!candidate || !simulation) return
    setIsWorking(true)
    setError('')
    try {
      const activatedVersion = candidate.version
      await activateRuleSet(activatedVersion)
      setCandidate(null)
      setSourceVersion(null)
      setHighlightedVersion(activatedVersion)
      setSimulation(null)
      await refreshRules()
    } catch (reason) {
      setError(getErrorMessage(reason))
    } finally {
      setIsWorking(false)
    }
  }

  /**
   * Reactivates a retained valid version without rewriting historical decisions.
   */
  async function handleRollback(version: number) {
    setIsWorking(true)
    setError('')
    try {
      await rollbackRuleSet(version)
      setCandidate(null)
      setSourceVersion(null)
      setHighlightedVersion(version)
      setSimulation(null)
      await refreshRules()
    } catch (reason) {
      setError(getErrorMessage(reason))
    } finally {
      setIsWorking(false)
    }
  }

  return (
    <div className="page">
      <section className="page-heading heading-with-actions">
        <div>
          <p className="eyebrow">Decision policy</p>
          <h1>Routing rules</h1>
          <p>Draft, validate, simulate, activate, monitor, and roll back constrained policy versions.</p>
        </div>
        <span className={canAdminister ? 'active-badge' : 'readonly-badge'}>
          <LockKey aria-hidden /> {canAdminister ? 'Administrator' : 'Read only'}
        </span>
      </section>
      {error && <p className="notice notice-error"><WarningCircle aria-hidden />{error}</p>}
      <section className="panel table-panel">
        <div className="panel-heading">
          <div><p className="section-kicker">Active policy</p><h2>Rule set version {ruleSet?.version ?? '—'}</h2></div>
          <span className="count-badge">{ruleSet?.rules.length ?? 0} rules</span>
        </div>
        <div className="table-scroll">
          <table>
            <thead><tr><th>Rule</th><th>Condition</th><th>Outcome</th><th>Technical ID</th></tr></thead>
            <tbody>
              {ruleSet?.rules.map(function renderFriendlyRuleRow(rule: ActiveRule) {
                return (
                  <tr key={rule.ruleId}>
                    <td><strong>{rule.input}</strong></td>
                    <td>{rule.condition}</td>
                    <td>{rule.outcome}</td>
                    <td className="data-cell">{rule.ruleId}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      </section>
      {canAdminister && (
        <section className="panel rule-editor">
          <div className="panel-heading">
            <div>
              <p className="section-kicker">Controlled change</p>
              <h2>
                {candidate
                  ? `Continue draft version ${candidate.version}`
                  : sourceVersion
                    ? `Create a new draft from version ${sourceVersion}`
                    : 'Create a typed draft'}
              </h2>
            </div>
            <span className="count-badge">No scripts</span>
          </div>
          <form className="form-grid" onSubmit={handleCreateDraft}>
            <label className="field">
              <span>Mail upper boundary</span>
              <span className="input-with-suffix">
                <Scales aria-hidden />
                <input name="mailUpperKilograms" type="number" min="0.01" step="0.01" value={draft.mailUpperKilograms} onChange={handleDraftChange} required />
                <small>kg</small>
              </span>
            </label>
            <label className="field">
              <span>Regular upper boundary</span>
              <span className="input-with-suffix">
                <Scales aria-hidden />
                <input name="regularUpperKilograms" type="number" min="0.01" step="0.01" value={draft.regularUpperKilograms} onChange={handleDraftChange} required />
                <small>kg</small>
              </span>
            </label>
            <label className="field field-wide">
              <span>Insurance threshold</span>
              <span className="input-with-suffix">
                <CurrencyEur aria-hidden />
                <input name="insuranceThresholdEuros" type="number" min="0" step="0.01" value={draft.insuranceThresholdEuros} onChange={handleDraftChange} required />
                <small>EUR</small>
              </span>
            </label>
            <div className="form-actions field-wide">
              <button type="submit" className="button button-primary" disabled={isWorking || Boolean(candidate)}>
                {candidate ? `Draft v${candidate.version} validated` : 'Validate and save draft'}
              </button>
              <button type="button" className="button button-secondary" onClick={handleSimulate} disabled={!candidate || isWorking}>Simulate differences</button>
              <button type="button" className="button button-primary" onClick={handleActivate} disabled={!simulation || isWorking}>Activate version</button>
            </div>
          </form>
          {(candidate || simulation) && (
            <div className="rule-workflow-status" aria-live="polite">
              {candidate && (
                <p className="notice">
                  <CheckCircle aria-hidden />
                  Draft version {candidate.version} is validated and loaded.
                  Simulate or activate it; editing a value creates a new immutable version.
                </p>
              )}
              {simulation && (
                <div className="simulation-summary">
                  <strong>{simulation.changedCount} of {simulation.sampleCount} representative outcomes would change.</strong>
                  {simulation.differences.map(difference => (
                    <p key={difference.sampleId}>
                      {difference.sampleId}: {difference.currentDepartment} / {difference.currentApprovalState}
                      {' → '}
                      {difference.proposedDepartment} / {difference.proposedApprovalState}
                    </p>
                  ))}
                </div>
              )}
            </div>
          )}
        </section>
      )}
      <section className="panel table-panel">
        <div className="panel-heading">
          <div><p className="section-kicker">Monitor and recover</p><h2>Version history</h2></div>
          <span className="count-badge">{versions.length} versions</span>
        </div>
        <div className="table-scroll">
          <table>
            <thead><tr><th>Version</th><th>Status</th><th>Created</th><th>Activated</th><th>Action</th></tr></thead>
            <tbody>
              {versions.map(version => (
                <tr
                  id={`rule-version-${version.version}`}
                  className={highlightedVersion === version.version ? 'rule-version-highlight' : undefined}
                  key={version.version}
                >
                  <td className="data-cell">v{version.version}</td>
                  <td><span className={version.status === 'Active' ? 'active-badge' : 'readonly-badge'}>{version.status}</span></td>
                  <td>{formatTimestamp(version.createdAtUtc)}</td>
                  <td>{version.activatedAtUtc ? formatTimestamp(version.activatedAtUtc) : 'Not activated'}</td>
                  <td>
                    {canAdminister ? (
                      <div className="table-actions">
                        {version.status === 'Draft' ? (
                          <button type="button" className="text-action" onClick={() => resumeDraft(version)} disabled={isWorking}>
                            Resume draft
                          </button>
                        ) : (
                          <button type="button" className="text-action" onClick={() => copyVersionToNewDraft(version)} disabled={isWorking}>
                            Use as new draft
                          </button>
                        )}
                        {version.status === 'Retired' && (
                          <button type="button" className="text-action" onClick={() => handleRollback(version.version)} disabled={isWorking}>
                            Roll back
                          </button>
                        )}
                      </div>
                    ) : '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}

/**
 * Loads newest-first privacy-safe audit events from durable storage.
 */
function Activity({
  onNavigate,
  onOpenDecision,
  onOpenBatch,
  onOpenRuleVersion,
}: {
  onNavigate: (view: View) => void
  onOpenDecision: (decisionId: string) => void
  onOpenBatch: (batchId: string) => void
  onOpenRuleVersion: (version: number) => void
}) {
  const [activity, setActivity] = useState<PagedResponse<ActivityRecord> | null>(null)
  const [range, setRange] = useState<OperationsTimeRange>('Recent')
  const [category, setCategory] = useState<ActivityCategory>('All')
  const [page, setPage] = useState(1)
  const [error, setError] = useState('')
  const [isLoading, setIsLoading] = useState(true)
  const events = activity?.items ?? []

  /**
   * Resets pagination whenever the audit window changes so the result always
   * opens at its newest matching events.
   */
  function handleRangeChange(nextRange: OperationsTimeRange) {
    setRange(nextRange)
    setPage(1)
  }

  /**
   * Applies one server-owned event family and resets paging so filtered counts,
   * pages, and visible records always describe the same result set.
   */
  function handleCategoryChange(nextCategory: ActivityCategory) {
    setCategory(nextCategory)
    setPage(1)
  }

  /**
   * Converts stable technical identifiers into clear operator language.
   */
  function getActivityLabel(eventType: string) {
    const labels: Record<string, string> = {
      'batch.created': 'XML manifest imported',
      'batch.row-completed': 'Parcel evaluated',
      'batch.row-failed': 'Parcel evaluation failed',
      'batch.row-deferred': 'Parcel evaluation deferred',
      'routing.decision-created': 'Routing decision created',
      'insurance.approved': 'Insurance approval recorded',
      'rule-set.draft-created': 'Rule draft created',
      'rule-set.activated': 'Rule version activated',
      'rule-set.rolled-back': 'Rule version rolled back',
    }
    return labels[eventType] ?? 'Operational event'
  }

  /**
   * Converts controlled event details into a concise operator explanation while
   * keeping raw identifiers inside the secondary technical disclosure.
   */
  function getActivityDescription(event: ActivityRecord) {
    if (event.eventType === 'batch.created') {
      const totalRows = Number.parseInt(event.details.totalRows ?? '0', 10)
      const failedRows = Number.parseInt(
        event.details.validationFailedRows ?? '0',
        10,
      )
      if (failedRows > 0) {
        return `${totalRows} rows accepted · ${failedRows} import issues recorded`
      }
      return `${totalRows} rows accepted for durable evaluation`
    }
    if (event.eventType === 'batch.row-failed') {
      return `Permanent row issue · ${event.details.errorCode ?? 'safe failure recorded'}`
    }
    if (event.eventType === 'batch.row-deferred') {
      return 'Temporary processing issue · row returned to the durable queue'
    }
    return null
  }

  useEffect(
    /**
     * Retrieves the bounded activity page and ignores late navigation updates.
     */
    function loadActivity() {
      let isCurrent = true
      setIsLoading(true)
      setError('')
      getActivity(range, page, category)
        .then(function applyActivity(result) {
          if (isCurrent) setActivity(result)
        })
        .catch(function applyActivityError(reason: unknown) {
          if (isCurrent) setError(getErrorMessage(reason))
        })
        .finally(function finishActivityLoad() {
          if (isCurrent) setIsLoading(false)
        })
      return function cancelActivityUpdate() {
        isCurrent = false
      }
    },
    [range, page, category],
  )

  /**
   * Resolves one explicit event destination. Navigation stays on a labelled
   * action so opening technical details cannot unexpectedly leave the page.
   */
  function getActivityAction(event: ActivityRecord) {
    if (event.relatedDecisionId) {
      const decisionId = event.relatedDecisionId
      return {
        label: 'View decision',
        open: () => onOpenDecision(decisionId),
      }
    }
    if (event.relatedBatchId) {
      const batchId = event.relatedBatchId
      return {
        label: 'Open import',
        open: () => onOpenBatch(batchId),
      }
    }
    if (event.eventType.startsWith('rule-set.')) {
      const ruleVersion = Number.parseInt(event.subjectId, 10)
      if (Number.isSafeInteger(ruleVersion) && ruleVersion > 0) {
        return {
          label: `View rule version ${ruleVersion}`,
          open: () => onOpenRuleVersion(ruleVersion),
        }
      }
      return {
        label: 'View rule versions',
        open: () => onNavigate('rules'),
      }
    }
    return null
  }

  /**
   * Renders one controlled audit record without request bodies or recipient data.
   */
  function renderActivityEvent(event: ActivityRecord) {
    const action = getActivityAction(event)
    const description = getActivityDescription(event)
    return (
      <article className="activity-row" key={event.id}>
        <span className="activity-row-icon"><ClockCounterClockwise aria-hidden /></span>
        <div>
          <strong>{getActivityLabel(event.eventType)}</strong>
          {description && <p className="activity-description">{description}</p>}
          <details className="event-technical">
            <summary>Technical details</summary>
            <span>{event.eventType} · {event.correlationId}</span>
            <span>{event.subjectType} · {event.subjectId.slice(0, 12)}</span>
          </details>
        </div>
        <div className="activity-meta">
          <span>{event.actorId}</span>
          <time dateTime={event.occurredAtUtc}>{formatTimestamp(event.occurredAtUtc)}</time>
          {action && (
            <button type="button" className="text-action activity-action" onClick={action.open}>
              {action.label} <ArrowRight aria-hidden />
            </button>
          )}
        </div>
      </article>
    )
  }

  return (
    <div className="page">
      <section className="page-heading">
        <p className="eyebrow">Audit trail</p>
        <h1>Activity</h1>
        <p>Trace persisted routing, import, approval, and rule lifecycle events.</p>
      </section>
      {error && <p className="notice notice-error"><WarningCircle aria-hidden />{error}</p>}
      <section className="panel activity-list">
        <div className="panel-heading history-heading">
          <div><p className="section-kicker">Newest first</p><h2>Operational events</h2></div>
          <div className="history-controls">
            <TimeRangeSelector
              value={range}
              onChange={handleRangeChange}
              recentLabel="Recent 15"
            />
            <FilterSelect
              id="activity-category-filter"
              label="Filter activity"
              value={category}
              options={activityCategoryOptions}
              onChange={value =>
                handleCategoryChange(value as ActivityCategory)}
            />
          </div>
        </div>
        <div className="history-summary">
          <span className="count-badge">
            {activity ? `${activity.totalItems} events` : 'Loading'}
          </span>
          {range !== 'Recent' && activity && activity.totalItems > 0 && (
            <span>Page {activity.page} of {activity.totalPages}</span>
          )}
        </div>
        {isLoading ? (
          <div className="activity-empty"><p>Loading activity…</p></div>
        ) : events.length > 0 ? (
          events.map(renderActivityEvent)
        ) : (
          <div className="activity-empty">
            <span className="activity-icon"><ClockCounterClockwise aria-hidden /></span>
            <h2>No activity recorded</h2>
            <p>Route a parcel or import an XML manifest to create durable events.</p>
          </div>
        )}
        {range !== 'Recent' && activity && (
          <Pagination
            page={activity.page}
            totalPages={activity.totalPages}
            onChange={setPage}
          />
        )}
      </section>
    </div>
  )
}

export default App
