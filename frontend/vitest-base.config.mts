import { defineConfig } from 'vitest/config';

// Component tests render real Angular Material components (harnesses, overlays): on a busy machine one can pass
// Vitest's 5 s default - a generous limit keeps a slow run from failing a correct test.
export default defineConfig({
  test: {
    testTimeout: 15_000,
  },
});
