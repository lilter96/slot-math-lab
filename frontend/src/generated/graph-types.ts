// Auto-generated from JSON Schema. DO NOT EDIT.
// Run `npm run generate-types` to regenerate.

export type ArrayOfInteger = number[];
export type ArrayOfString = string[];
export type ArrayOfNode = {
  id: string;
  label?: null | string;
  inputs?: DictionaryOfStringAndPort;
  outputs?: DictionaryOfStringAndPort;
  [k: string]: unknown;
}[];
export type ArrayOfEdge = {
  id: string;
  sourceNodeId: string;
  sourcePort: string;
  targetNodeId: string;
  targetPort: string;
  [k: string]: unknown;
}[];
export type ArrayOfPluginReference = {
  pluginId: string;
  contract: 'IEvaluator' | 'ITransform' | 'WeightSource';
  version?: null | string;
  config?: null | {
    [k: string]: string;
  };
  [k: string]: unknown;
}[];

/**
 * Canonical schema for slot math graph configuration.
 */
export interface SlotMathLabGraphSchema {
  schemaVersion: string;
  id?: null | string;
  name?: null | string;
  description?: null | string;
  evidenceInputs?:
    | null
    | {
        kind?: string;
        id?: string;
        version?: string;
        sha256?: string;
        uri?: null | string;
        contentBase64?: null | string;
        [k: string]: unknown;
      }[];
  symbols?: {
    id: string;
    name: string;
    kind?: 'Standard' | 'Wild' | 'Scatter' | 'Bonus' | 'Multiplier' | 'Money' | 'Jackpot';
    properties?: null | {
      [k: string]: string;
    };
    [k: string]: unknown;
  }[];
  paytables?: {
    id: string;
    entries: {
      symbolId: string;
      counts: ArrayOfInteger;
      payouts: ArrayOfString;
      [k: string]: unknown;
    }[];
    [k: string]: unknown;
  }[];
  paylineSets?: {
    id: string;
    paylines: {
      positions: ArrayOfInteger;
      [k: string]: unknown;
    }[];
    [k: string]: unknown;
  }[];
  reelStrips?: {
    id: string;
    name: string;
    symbols: ArrayOfString;
    [k: string]: unknown;
  }[];
  reelSets?: {
    id: string;
    name: string;
    stripIds: ArrayOfString;
    [k: string]: unknown;
  }[];
  boardConfig?: null | {
    rows?: number;
    columns?: number;
    allowMultiSymbol?: boolean;
    allowEmpty?: boolean;
    allowLocked?: boolean;
    growable?: boolean;
    [k: string]: unknown;
  };
  initialState?: null | {
    [k: string]: JsonElement;
  };
  nodes?: ArrayOfNode;
  edges?: ArrayOfEdge;
  expressions?: null | {
    [k: string]: Expression;
  };
  stateSchema?: {
    name: string;
    type?: null | string;
    [k: string]: unknown;
  }[];
  mechanics?: null | {
    [k: string]: CustomMechanic;
  };
  plugins?: ArrayOfPluginReference;
  [k: string]: unknown;
}
export interface JsonElement {
  valueKind?: 'Undefined' | 'Object' | 'Array' | 'String' | 'Number' | 'True' | 'False' | 'Null';
  [k: string]: unknown;
}
export interface DictionaryOfStringAndPort {
  [k: string]: {
    name: string;
    type?: 'Board' | 'State' | 'Weights' | 'Wins' | 'Number' | 'Boolean' | 'String' | 'Symbol' | 'Trigger';
    defaultValue?: null | {
      annotation?: null | string;
      [k: string]: unknown;
    };
    [k: string]: unknown;
  };
}
export interface Expression {
  annotation?: null | string;
  [k: string]: unknown;
}
export interface CustomMechanic {
  initialState?: null | {
    [k: string]: JsonElement;
  };
  name: string;
  description?: null | string;
  nodes?: ArrayOfNode;
  edges?: ArrayOfEdge;
  expressions?: null | {
    [k: string]: Expression;
  };
  plugins?: ArrayOfPluginReference;
  [k: string]: unknown;
}
