import { createRequire, registerHooks } from 'node:module';
import { realpathSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const modulesRootValue = process.env.COMPANY_HARNESS_RUNTIME_MODULES;
if (!modulesRootValue) {
  throw new Error('COMPANY_HARNESS_RUNTIME_MODULES is required.');
}

const modulesRoot = realpathSync(modulesRootValue);
const modulesRootPrefix = `${modulesRoot}${path.sep}`;
const runtimeRequire = createRequire(
  pathToFileURL(path.join(path.dirname(modulesRoot), 'package.json')),
);

function isBareSpecifier(specifier) {
  return !specifier.startsWith('.')
    && !specifier.startsWith('/')
    && !specifier.startsWith('file:')
    && !specifier.startsWith('node:')
    && !path.isAbsolute(specifier);
}

registerHooks({
  resolve(specifier, context, nextResolve) {
    try {
      return nextResolve(specifier, context);
    } catch (originalError) {
      if (!isBareSpecifier(specifier)) {
        throw originalError;
      }

      let resolvedPath;
      try {
        resolvedPath = realpathSync(runtimeRequire.resolve(specifier));
      } catch {
        throw originalError;
      }

      if (resolvedPath !== modulesRoot && !resolvedPath.startsWith(modulesRootPrefix)) {
        throw originalError;
      }

      return {
        url: pathToFileURL(resolvedPath).href,
        shortCircuit: true,
      };
    }
  },
});
