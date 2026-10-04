import type { AiModel, NovelAiTier } from '../api/api';

const TIER_RANK: Record<NovelAiTier, number> = {
  Unknown: -1,
  Paper: 0,
  Tablet: 1,
  Scroll: 2,
  Opus: 3,
};

export function tierRank(tier: NovelAiTier): number {
  return TIER_RANK[tier] ?? -1;
}

/** True when the account's tier is at least the tier a model requires. */
export function isTierEligible(userTier: NovelAiTier, required: NovelAiTier): boolean {
  return tierRank(userTier) >= tierRank(required);
}

export function tierLabel(tier: NovelAiTier): string {
  return tier === 'Unknown' ? 'No tier' : tier;
}

export function eligibleModels(
  models: AiModel[],
  kind: AiModel['kind'],
  userTier: NovelAiTier,
): AiModel[] {
  return models.filter(
    (model) => model.kind === kind && isTierEligible(userTier, model.minTier),
  );
}

export function contextTokensFor(model: AiModel, userTier: NovelAiTier): number | null {
  const override = model.tierOverrides?.[userTier];
  if (typeof override === 'number') {
    return override;
  }
  const tokens = model.contextTokens;
  return typeof tokens === 'number' ? tokens : null;
}
