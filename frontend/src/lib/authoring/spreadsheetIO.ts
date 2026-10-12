import {parseCsv} from '../weightCsv';

export interface SheetCell {
    text: string;
    address?: string;
    error?: string
}

export interface InputSheet {
    name: string;
    rows: SheetCell[][]
}

export interface InputWorkbook {
    sheets: InputSheet[];
    format: 'xlsx' | 'csv' | 'tsv'
}

export const spreadsheetLimits = {
    bytes: 10 * 1024 * 1024,
    inflated: 32 * 1024 * 1024,
    rows: 20000,
    columns: 128,
    cells: 100000
};

export function parseTableText(text: string, format: 'csv' | 'tsv'): InputWorkbook {
    if (text.length > 1048576) throw new Error('Text imports are limited to 1 MiB.');
    const rows = parseCsv(text, format === 'tsv' ? '\t' : ',', spreadsheetLimits.rows);
    checkSize(rows.length, Math.max(0, ...rows.map(row => row.length)), rows.reduce((count, row) => count + row.length, 0));
    return {format, sheets: [{name: 'Pasted table', rows: rows.map(row => row.map(text => ({text})))}]};
}

export function tableText(rows: readonly (readonly string[])[], format: 'csv' | 'tsv'): string {
    const separator = format === 'csv' ? ',' : '\t', quote = (value: string) => `"${value.replaceAll('"', '""')}"`;
    return rows.map(row => row.map(quote).join(separator)).join('\r\n');
}

/** Expand the original XML decimal token, never the parser's already-rounded Number. */
export function spreadsheetNumber(text: string): string {
    const match = /^([+-]?)(\d+)(?:\.(\d*))?(?:[eE]([+-]?\d+))?$/.exec(text);
    if (!match || text.length > 256) throw new Error('Unsupported exact numeric cell; use literal text.');
    const exponent = Number(match[4] ?? 0);
    if (!Number.isSafeInteger(exponent) || Math.abs(exponent) > 256) throw new Error('Numeric exponent exceeds the supported exact range.');
    const digits = match[2] + (match[3] ?? ''), decimal = match[2].length + exponent;
    const value = decimal <= 0 ? `0.${'0'.repeat(-decimal)}${digits}` : decimal >= digits.length ? digits + '0'.repeat(decimal - digits.length) : `${digits.slice(0, decimal)}.${digits.slice(decimal)}`;
    return (match[1] === '-' ? '-' : '') + value;
}

function checkSize(rows: number, columns: number, cells: number) {
    if (rows > spreadsheetLimits.rows || columns > spreadsheetLimits.columns || cells > spreadsheetLimits.cells) throw new Error('Import exceeds 20,000 rows, 128 columns or 100,000 cells. Split the table.');
}

/** Check ZIP declarations before decompression; this importer accepts genuine OpenXML .xlsx files. */
export function checkSpreadsheetZip(bytes: Uint8Array): void {
    if (bytes.length > spreadsheetLimits.bytes || bytes.length < 22 || bytes[0] !== 0x50 || bytes[1] !== 0x4b) throw new Error('Choose a genuine .xlsx workbook no larger than 10 MiB.');
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    let end = bytes.length - 22;
    while (end >= Math.max(0, bytes.length - 65557) && view.getUint32(end, true) !== 0x06054b50) end--;
    if (end < 0 || end < bytes.length - 65557) throw new Error('The workbook ZIP directory is missing.');
    const count = view.getUint16(end + 10, true), start = view.getUint32(end + 16, true);
    if (count > 2048 || count === 0xffff || start >= end || view.getUint16(end + 4, true) !== 0 || view.getUint16(end + 6, true) !== 0) throw new Error('Unsupported workbook archive layout.');
    let offset = start, inflated = 0;
    for (let entry = 0; entry < count; entry++) {
        if (offset + 46 > end || view.getUint32(offset, true) !== 0x02014b50) throw new Error('The workbook ZIP directory is malformed.');
        const flags = view.getUint16(offset + 8, true), method = view.getUint16(offset + 10, true),
            size = view.getUint32(offset + 24, true);
        const nameLength = view.getUint16(offset + 28, true), extra = view.getUint16(offset + 30, true),
            comment = view.getUint16(offset + 32, true);
        if (offset + 46 + nameLength + extra + comment > end || flags & 1 || ![0, 8].includes(method)) throw new Error('Encrypted or unsupported workbook archive entry.');
        const name = new TextDecoder().decode(bytes.subarray(offset + 46, offset + 46 + nameLength));
        if (name.startsWith('/') || name.split('/').includes('..') || /vbaProject\.bin$/i.test(name)) throw new Error('This workbook contains unsupported paths or macros. Export a plain .xlsx file.');
        inflated += size;
        if (inflated > spreadsheetLimits.inflated) throw new Error('The expanded workbook exceeds 32 MiB. Split the data.');
        offset += 46 + nameLength + extra + comment;
    }
}

const xml = (text: string) => {
    if (/<!DOCTYPE|<!ENTITY/i.test(text)) throw new Error('Workbook XML entities are unsupported.');
    const document = new DOMParser().parseFromString(text, 'application/xml');
    if (document.getElementsByTagName('parsererror').length) throw new Error('Malformed workbook XML.');
    return document;
};
const elements = (document: Document | Element, tag: string) => [...document.getElementsByTagNameNS('*', tag)];

export async function readSpreadsheet(bytes: Uint8Array): Promise<InputWorkbook> {
    checkSpreadsheetZip(bytes);
    const XLSX = await import('xlsx');
    const workbook = XLSX.read(bytes, {
        type: 'array',
        bookFiles: true,
        cellFormula: true,
        cellText: false,
        cellHTML: false,
        cellDates: true,
        bookVBA: false,
        nodim: true,
        WTF: true
    });
    const files = (workbook as typeof workbook & { files: Record<string, { content: Uint8Array | string }> }).files;
    const readFile = (name: string) => {
        const file = files?.[name];
        if (!file) throw new Error(`Missing workbook part: ${name}.`);
        return typeof file.content === 'string' ? file.content : new TextDecoder().decode(file.content);
    };
    const book = xml(readFile('xl/workbook.xml')), relationships = xml(readFile('xl/_rels/workbook.xml.rels'));
    const paths = new Map(elements(relationships, 'Relationship').map(element => [element.getAttribute('Id'), element.getAttribute('Target')]));
    const resolve = (target: string) => {
        const parts: string[] = [];
        for (const part of (target.startsWith('/') ? target.slice(1) : `xl/${target}`).split('/')) {
            if (part === '..') {
                if (!parts.length) throw new Error('Unsupported worksheet path.');
                parts.pop();
            } else if (part !== '.') parts.push(part);
        }
        return parts.join('/');
    };
    const sheets: InputSheet[] = [];
    let totalCells = 0;
    for (const element of elements(book, 'sheet')) {
        const name = element.getAttribute('name')!,
            relation = element.getAttributeNS('http://schemas.openxmlformats.org/officeDocument/2006/relationships', 'id') ?? element.getAttribute('r:id'),
            target = paths.get(relation);
        if (!name || !target || !workbook.Sheets[name]) throw new Error('The worksheet relationship could not be resolved.');
        const source = xml(readFile(resolve(target))), rows: SheetCell[][] = [];
        for (const cell of elements(source, 'c')) {
            const address = cell.getAttribute('r');
            if (!address) throw new Error('A workbook cell has no address.');
            const coordinate = XLSX.utils.decode_cell(address);
            checkSize(coordinate.r + 1, coordinate.c + 1, ++totalCells);
            const parsed = workbook.Sheets[name][address], value = elements(cell, 'v')[0]?.textContent;
            let text = '', error: string | undefined;
            if (elements(cell, 'f').length || parsed?.f) error = 'Formula cell rejected. Replace it with an exact literal value; cached results are not imported.';
            else if (parsed?.t === 'n') {
                try {
                    text = spreadsheetNumber(value ?? '');
                } catch (failure) {
                    error = failure instanceof Error ? failure.message : 'Unsupported exact number.';
                }
            } else if (parsed?.t === 's' || parsed?.t === 'str') text = String(parsed.v ?? '');
            else if (parsed && parsed.t !== 'z') error = 'Dates, booleans and error cells are not mathematical input. Use an exact literal.';
            rows[coordinate.r] ??= [];
            rows[coordinate.r][coordinate.c] = {text, address, ...(error ? {error} : {})};
        }
        const columns = Math.max(0, ...rows.filter(Boolean).map(row => row.length));
        checkSize(rows.length, columns, rows.length * columns);
        sheets.push({
            name,
            rows: Array.from({length: rows.length}, (_, index) => Array.from({length: columns}, (_, column) => rows[index]?.[column] ?? {text: ''}))
        });
    }
    if (!sheets.length || sheets.length > 64) throw new Error('Choose a workbook with 1–64 worksheets.');
    return {format: 'xlsx', sheets};
}

export async function writeSpreadsheet(rows: string[][], name = 'Inputs', notes: string[][] = []): Promise<Uint8Array> {
    const XLSX = await import('xlsx'), workbook = XLSX.utils.book_new();
    // Every value, including IDs and exact fractions, is a text cell. No formula/date inference.
    XLSX.utils.book_append_sheet(workbook, XLSX.utils.aoa_to_sheet(rows), name.slice(0, 31));
    if (notes.length) XLSX.utils.book_append_sheet(workbook, XLSX.utils.aoa_to_sheet(notes), 'Read me');
    return XLSX.write(workbook, {type: 'array', bookType: 'xlsx', compression: true}) as Uint8Array;
}
