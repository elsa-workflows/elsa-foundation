// @ts-check
import js from '@eslint/js';
import tseslint from 'typescript-eslint';
import reactHooks from 'eslint-plugin-react-hooks';
import jsxA11y from 'eslint-plugin-jsx-a11y';
import globals from 'globals';

/**
 * Root flat ESLint config for all workspace packages (issue #185).
 *
 * Scope: a fast correctness gate, not a formatter. We run typescript-eslint
 * in its non-type-checked "recommended" mode to keep lint fast across the
 * 13 client packages. Stylistic rules are intentionally omitted.
 */
export default tseslint.config(
  {
    // Global ignores — must be a standalone object (no other keys) to apply repo-wide.
    ignores: [
      '**/dist/**',
      '**/node_modules/**',
      '**/wwwroot/**',
      '**/build/**',
      '**/coverage/**',
      '**/*.min.js',
      '**/vite-env.d.ts',
    ],
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ['**/*.{ts,tsx}'],
    plugins: {
      'react-hooks': reactHooks,
      'jsx-a11y': jsxA11y,
    },
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: 'module',
      globals: {
        ...globals.browser,
        ...globals.es2022,
      },
      parserOptions: {
        ecmaFeatures: { jsx: true },
      },
    },
    rules: {
      // Rules of Hooks is a hard correctness error.
      'react-hooks/rules-of-hooks': 'error',
      // Exhaustive-deps stays a warning: a 552-line effect-heavy hook exists and
      // mass dependency-array refactors are out of scope for this gate (#185).
      'react-hooks/exhaustive-deps': 'warn',
      // Keep an existing jsx-a11y/no-autofocus disable directive meaningful.
      'jsx-a11y/no-autofocus': 'warn',
      // Allow intentionally-unused args/vars when prefixed with `_`.
      '@typescript-eslint/no-unused-vars': [
        'warn',
        {
          argsIgnorePattern: '^_',
          varsIgnorePattern: '^_',
          caughtErrorsIgnorePattern: '^_',
          ignoreRestSiblings: true,
        },
      ],
      // Downgraded to warn: the SDK type surface intentionally uses named
      // `interface X extends Y {}` aliases for readable public contracts, and
      // those files are being edited by other agents concurrently (#185).
      '@typescript-eslint/no-empty-object-type': 'warn',
    },
  },
  {
    // Test files legitimately reach for `any` when stubbing SDK/network shapes;
    // keep it a warning there so the correctness gate stays strict for app code.
    files: ['**/__tests__/**', '**/*.{test,spec}.{ts,tsx}'],
    rules: {
      '@typescript-eslint/no-explicit-any': 'warn',
    },
  },
  {
    // Config and Node-side tooling files run in a Node environment.
    files: ['**/*.config.{js,ts,mjs,cjs}', '**/vite.*.config.ts'],
    languageOptions: {
      globals: {
        ...globals.node,
      },
    },
  },
);
