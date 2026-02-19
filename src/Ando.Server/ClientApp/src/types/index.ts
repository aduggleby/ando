// =============================================================================
// types/index.ts
//
// TypeScript type definitions matching the FastEndpoints contracts.
// =============================================================================

// Auth types
export interface UserDto {
  id: number;
  email: string;
  displayName: string | null;
  emailVerified: boolean;
  isAdmin: boolean;
  avatarUrl: string | null;
}

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe: boolean;
}

export interface LoginResponse {
  success: boolean;
  error: string | null;
  user: UserDto | null;
}

export interface RegisterRequest {
  email: string;
  displayName: string | null;
  password: string;
  confirmPassword: string;
}

export interface RegisterResponse {
  success: boolean;
  error: string | null;
  isFirstUser: boolean;
  user: UserDto | null;
}

export interface GetMeResponse {
  isAuthenticated: boolean;
  user: UserDto | null;
}

// Project types
export interface ProjectListItemDto {
  id: number;
  repoFullName: string;
  repoUrl: string;
  createdAt: string;
  lastBuildAt: string | null;
  lastBuildStatus: string | null;
  lastBuildGitVersionTag: string | null;
  totalBuilds: number;
  isConfigured: boolean;
  missingSecretsCount: number;
}

export interface ProjectDetailsDto {
  id: number;
  repoFullName: string;
  repoUrl: string;
  defaultBranch: string;
  branchFilter: string;
  enablePrBuilds: boolean;
  timeoutMinutes: number;
  createdAt: string;
  lastBuildAt: string | null;
  totalBuilds: number;
  successfulBuilds: number;
  failedBuilds: number;
  isConfigured: boolean;
  missingSecrets: string[];
  missingSecretsCount: number;
  recentBuilds: BuildListItemDto[];
}

export interface ProjectSettingsDto {
  id: number;
  repoFullName: string;
  branchFilter: string;
  enablePrBuilds: boolean;
  timeoutMinutes: number;
  dockerImage: string | null;
  profile: string | null;
  availableProfiles: string[];
  isProfileValid: boolean;
  notifyOnFailure: boolean;
  notificationEmail: string | null;
  requiredSecrets: SecretStatusDto[];
  allSecrets: SecretDto[];
  missingSecrets: string[];
}

export interface SecretStatusDto {
  name: string;
  isSet: boolean;
}

export interface SecretDto {
  name: string;
  updatedAt: string;
}

export interface ProjectStatusDto {
  id: number;
  repoFullName: string;
  repoUrl: string;
  createdAt: string;
  lastDeploymentAt: string | null;
  deploymentStatus: 'NotDeployed' | 'Failed' | 'Deployed';
  totalBuilds: number;
}

export interface CreateProjectRequest {
  repoFullName: string;
}

export interface CreateProjectResponse {
  success: boolean;
  projectId: number | null;
  error: string | null;
  redirectUrl: string | null;
  project?: {
    id: number;
    repoFullName: string;
  };
}

export interface UpdateProjectSettingsRequest {
  branchFilter: string;
  enablePrBuilds: boolean;
  timeoutMinutes: number;
  dockerImage: string | null;
  profile: string | null;
  notifyOnFailure: boolean;
  notificationEmail: string | null;
}

// Build types
export interface BuildListItemDto {
  id: number;
  commitSha: string;
  shortCommitSha: string;
  gitVersionTag: string | null;
  branch: string;
  commitMessage: string | null;
  commitAuthor: string | null;
  status: BuildStatus;
  trigger: BuildTrigger;
  queuedAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  duration: string | null;
  pullRequestNumber: number | null;
}

export interface BuildDetailsDto {
  id: number;
  projectId: number;
  projectName: string;
  projectUrl: string;
  commitSha: string;
  shortCommitSha: string;
  branch: string;
  commitMessage: string | null;
  commitAuthor: string | null;
  pullRequestNumber: number | null;
  status: string;
  trigger: BuildTrigger;
  triggeredBy: string | null;
  queuedAt: string;
  startedAt: string | null;
  finishedAt: string | null;
  duration: string | null;
  stepsTotal: number;
  stepsCompleted: number;
  stepsFailed: number;
  errorMessage: string | null;
  canCancel: boolean;
  canRetry: boolean;
  isLive: boolean;
  logEntries: LogEntryDto[];
  artifacts: ArtifactDto[];
}

export interface LogEntryDto {
  id: number;
  sequence: number;
  level: string;
  message: string;
  stepName: string | null;
  timestamp: string;
}

// Alias for components that use LogEntry
export type LogEntry = LogEntryDto;

export interface ArtifactDto {
  id: number;
  fileName: string;
  fileSize: number;
  formattedSize: string;
  createdAt: string;
}

export type BuildStatus = 'Queued' | 'Running' | 'Success' | 'Failed' | 'Cancelled' | 'TimedOut';
export type BuildTrigger = 'Push' | 'PullRequest' | 'Manual';

// Dashboard types
export interface DashboardDto {
  recentBuilds: RecentBuildItemDto[];
  totalProjects: number;
  buildsToday: number;
  failedToday: number;
}

export interface RecentBuildItemDto {
  id: number;
  projectName: string;
  branch: string;
  shortCommitSha: string;
  gitVersionTag: string | null;
  status: string;
  startedAt: string | null;
  duration: string | null;
}

// Admin types
export interface AdminDashboardDto {
  totalUsers: number;
  verifiedUsers: number;
  unverifiedUsers: number;
  adminUsers: number;
  totalProjects: number;
  totalBuilds: number;
  activeBuilds: number;
  recentBuilds: number;
  recentUsers: RecentUserDto[];
  recentBuilds24h: RecentBuildDto[];
}

export interface RecentUserDto {
  id: number;
  email: string;
  displayName: string;
  createdAt: string;
  emailVerified: boolean;
}

export interface RecentBuildDto {
  id: number;
  projectName: string;
  branch: string;
  status: string;
  createdAt: string;
}

export interface UserListItemDto {
  id: number;
  email: string;
  displayName: string;
  emailVerified: boolean;
  isAdmin: boolean;
  isLockedOut: boolean;
  createdAt: string;
  lastLoginAt: string | null;
  hasGitHubConnection: boolean;
  projectCount: number;
}

export interface UserDetailsDto {
  id: number;
  email: string;
  displayName: string | null;
  avatarUrl: string | null;
  emailVerified: boolean;
  emailVerificationSentAt: string | null;
  isAdmin: boolean;
  isLockedOut: boolean;
  lockoutEnd: string | null;
  createdAt: string;
  lastLoginAt: string | null;
  hasGitHubConnection: boolean;
  gitHubLogin: string | null;
  gitHubConnectedAt: string | null;
  projects: UserProjectDto[];
  totalBuilds: number;
}

export interface UserProjectDto {
  id: number;
  name: string;
  description: string | null;
  createdAt: string;
  buildCount: number;
}

// Admin project type
export interface AdminProjectDto {
  id: number;
  repoFullName: string;
  ownerEmail: string;
  totalBuilds: number;
  lastBuildStatus: string | null;
  lastBuildAt: string | null;
  isConfigured: boolean;
}

// API Response wrappers
export interface ApiResponse<T> {
  success: boolean;
  error?: string;
  data?: T;
}
