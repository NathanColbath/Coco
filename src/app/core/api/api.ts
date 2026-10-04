import createClient, { type Middleware } from 'openapi-fetch';
import type { components, paths } from './schema';

export type { paths, components };
export type AiModel = components['schemas']['AiModelDto'];
export type AiModelKind = components['schemas']['AiModelKind'];
export type NovelAiTier = components['schemas']['NovelAiTier'];
export type ModelCapabilities = components['schemas']['ModelCapabilities'];
export type User = components['schemas']['UserDto'];
export type NovelAiConnection = components['schemas']['NovelAiConnectionDto'];
export type StorySummary = components['schemas']['StorySummaryDto'];
export type StoryDetail = components['schemas']['StoryDetailDto'];
export type StoryBlock = components['schemas']['StoryBlockDto'];
export type LorebookEntry = components['schemas']['LorebookEntryDto'];
export type ContextTrace = components['schemas']['ContextTrace'];
export type ContextSegment = components['schemas']['ContextSegment'];
export type StoryGeneration = components['schemas']['GenerationDto'];
export type StoryContextSettings = components['schemas']['StoryContextSettings'];
export type TextSamplerSettings = components['schemas']['TextSamplerSettings'];
export type ComicSummary = components['schemas']['ComicSummaryDto'];
export type ComicDetail = components['schemas']['ComicDetailDto'];
export type Page = components['schemas']['PageDto'];
export type DialogueLine = components['schemas']['DialogueLineDto'];
export type StoryCharacter = components['schemas']['CharacterDto'];
export type CharacterSheet = components['schemas']['CharacterSheetDto'];
export type Cover = components['schemas']['CoverDto'];
export type PageAttempt = components['schemas']['PageAttemptDto'];
export type LayoutTemplate = components['schemas']['LayoutTemplateDto'];
export type ComicStatus = components['schemas']['ComicStatus'];
export type ContentRating = components['schemas']['ContentRating'];
export type DialogueKind = components['schemas']['DialogueKind'];
export type ReadingDirection = components['schemas']['ReadingDirection'];
export type ColorMode = components['schemas']['ColorMode'];

/** Error carrying the API's problem-detail message so features can surface it. */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

let authToken: string | null = null;
let onUnauthorized: (() => void) | null = null;

export function setApiAuthToken(token: string | null): void {
  authToken = token;
}

/**
 * The current token, for callers that cannot use the API client.
 * Image requests are a browser-level `<img>` load, which carries no headers,
 * so they are fetched explicitly and turned into object URLs instead.
 */
export function getApiAuthToken(): string | null {
  return authToken;
}

/** Called when any request comes back 401 so the session can be cleared once. */
export function setUnauthorizedHandler(handler: () => void): void {
  onUnauthorized = handler;
}

const authMiddleware: Middleware = {
  onRequest({ request }) {
    if (authToken && !request.headers.has('Authorization')) {
      request.headers.set('Authorization', `Bearer ${authToken}`);
    }
    return request;
  },
  onResponse({ response }) {
    if (response.status === 401) {
      onUnauthorized?.();
    }
    return response;
  },
};

export const api = createClient<paths>();
api.use(authMiddleware);

/** Unwraps an openapi-fetch result, throwing a typed error on failure. */
export async function unwrap<T>(
  result: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<T> {
  const { data, error, response } = await result;
  if (error !== undefined || !response.ok) {
    throw new ApiError(extractMessage(error), response.status);
  }
  return data as T;
}

function extractMessage(error: unknown): string {
  if (typeof error === 'string') {
    return error;
  }
  if (error && typeof error === 'object') {
    const problem = error as { detail?: string; title?: string };
    return problem.detail ?? problem.title ?? 'Something went wrong.';
  }
  return 'Something went wrong.';
}
