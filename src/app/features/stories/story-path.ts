import type { StoryBlock } from '../../core/api/api';

/**
 * Mirrors the server's StoryPath: walks the block tree from the cursor back to
 * the root so the editor shows the active branch, not every alternative.
 */
export function activePath(blocks: StoryBlock[], cursorId: string | null): StoryBlock[] {
  if (!cursorId) {
    return [];
  }

  const byId = new Map(blocks.map((block) => [block.id, block]));
  const path: StoryBlock[] = [];
  let current: string | null = cursorId;
  let guard = 0;

  while (current && guard++ < 10_000) {
    const block = byId.get(current);
    if (!block) {
      break;
    }
    path.push(block);
    current = block.parentId ?? null;
  }

  return path.reverse();
}

/** Blocks that are not on the active branch — the retries and abandoned takes. */
export function alternativeCount(blocks: StoryBlock[], active: StoryBlock[]): number {
  const activeIds = new Set(active.map((block) => block.id));
  return blocks.filter((block) => !activeIds.has(block.id)).length;
}

export function wordCount(text: string): number {
  return text.split(/\s+/).filter(Boolean).length;
}
