import type { AiModel } from '../api/api';
import {
  contextTokensFor,
  eligibleModels,
  isTierEligible,
  tierRank,
} from './model-registry';

function model(overrides: Partial<AiModel>): AiModel {
  return {
    id: 'test',
    kind: 'Text',
    displayName: 'Test',
    family: 'Test',
    minTier: 'Tablet',
    isDefault: false,
    isDeprecated: false,
    capabilities: {},
    ...overrides,
  } as AiModel;
}

describe('model-registry', () => {
  it('ranks tiers so Paper is below Opus', () => {
    expect(tierRank('Paper')).toBeLessThan(tierRank('Opus'));
    expect(tierRank('Unknown')).toBeLessThan(tierRank('Paper'));
  });

  it('gates models by tier', () => {
    expect(isTierEligible('Opus', 'Opus')).toBe(true);
    expect(isTierEligible('Scroll', 'Opus')).toBe(false);
    expect(isTierEligible('Tablet', 'Tablet')).toBe(true);
  });

  it('filters the registry by kind and tier', () => {
    const models = [
      model({ id: 'glm', kind: 'Text', minTier: 'Tablet' }),
      model({ id: 'xialong', kind: 'Text', minTier: 'Opus' }),
      model({ id: 'v5', kind: 'Image', minTier: 'Tablet' }),
    ];

    expect(eligibleModels(models, 'Text', 'Scroll').map((m) => m.id)).toEqual(['glm']);
    expect(eligibleModels(models, 'Image', 'Tablet').map((m) => m.id)).toEqual(['v5']);
  });

  it('applies per-tier context overrides', () => {
    const kayra = model({
      id: 'kayra-v1',
      contextTokens: 8192,
      tierOverrides: { Tablet: 3072, Scroll: 6144, Opus: 8192 },
    });

    expect(contextTokensFor(kayra, 'Tablet')).toBe(3072);
    expect(contextTokensFor(kayra, 'Scroll')).toBe(6144);
    expect(contextTokensFor(kayra, 'Opus')).toBe(8192);
    expect(contextTokensFor(model({ contextTokens: 28672 }), 'Opus')).toBe(28672);
  });
});
