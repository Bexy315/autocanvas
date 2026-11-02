import schema from '../../../../schema/automation.v1.json';
import type { Schema } from './schemaKeys';

/**
 * The JSON Schema of the automation format (schema/automation.v1.json), the same file the docs
 * publish: the editor completes keys and values from it.
 */
export const automationSchema = schema as Schema;
