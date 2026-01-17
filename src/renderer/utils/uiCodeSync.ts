import type { UIElement, UIStyle, UILayoutData } from '../../shared/types';

type StyleMap = Record<string, string>;

const STYLE_KEY_MAP: Record<string, keyof UIStyle> = {
  width: 'width',
  height: 'height',
  'background-color': 'backgroundColor',
  color: 'color',
  opacity: 'opacity',
  'border-radius': 'borderRadius',
  'border-width': 'borderWidth',
  'border-color': 'borderColor',
  'font-size': 'fontSize',
  'font-weight': 'fontWeight',
  'text-align': 'textAlign',
  'justify-content': 'justifyContent',
  'align-items': 'alignItems',
  gap: 'gap',
};

const ELEMENT_TAG_MAP: Record<UIElement['type'], string> = {
  Panel: 'div',
  Text: 'span',
  Button: 'button',
  Image: 'img',
  Slider: 'input',
  Input: 'input',
  ScrollView: 'div',
  List: 'div',
};

function toCssSize(value?: number | string): string | undefined {
  if (value === undefined || value === null) return undefined;
  if (typeof value === 'number') return `${value}px`;
  return value;
}

function spacingToCss(spacing?: { top: number; right: number; bottom: number; left: number }): string | undefined {
  if (!spacing) return undefined;
  const { top, right, bottom, left } = spacing;
  if (top === right && top === bottom && top === left) return `${top}px`;
  if (top === bottom && right === left) return `${top}px ${right}px`;
  return `${top}px ${right}px ${bottom}px ${left}px`;
}

function cssToSpacing(value: string): { top: number; right: number; bottom: number; left: number } | undefined {
  const parts = value.split(/\s+/).map((v) => v.trim()).filter(Boolean);
  const nums = parts.map((p) => parseFloat(p));
  if (nums.length === 1) {
    return { top: nums[0], right: nums[0], bottom: nums[0], left: nums[0] };
  }
  if (nums.length === 2) {
    return { top: nums[0], right: nums[1], bottom: nums[0], left: nums[1] };
  }
  if (nums.length === 3) {
    return { top: nums[0], right: nums[1], bottom: nums[2], left: nums[1] };
  }
  if (nums.length >= 4) {
    return { top: nums[0], right: nums[1], bottom: nums[2], left: nums[3] };
  }
  return undefined;
}

function serializeStyle(style: UIStyle, layout?: UIElement['layout']): string {
  const entries: string[] = [];

  const width = toCssSize(style.width);
  if (width) entries.push(`width:${width}`);
  const height = toCssSize(style.height);
  if (height) entries.push(`height:${height}`);

  if (style.backgroundColor) entries.push(`background-color:${style.backgroundColor}`);
  if (style.color) entries.push(`color:${style.color}`);
  if (style.opacity !== undefined) entries.push(`opacity:${style.opacity}`);

  if (style.borderRadius !== undefined) entries.push(`border-radius:${style.borderRadius}px`);
  if (style.borderWidth !== undefined) entries.push(`border-width:${style.borderWidth}px`);
  if (style.borderColor) entries.push(`border-color:${style.borderColor}`);

  if (style.fontSize !== undefined) entries.push(`font-size:${style.fontSize}px`);
  if (style.fontWeight) entries.push(`font-weight:${style.fontWeight}`);
  if (style.textAlign) entries.push(`text-align:${style.textAlign}`);

  if (style.justifyContent) entries.push(`justify-content:${style.justifyContent}`);
  if (style.alignItems) entries.push(`align-items:${style.alignItems}`);
  if (style.gap !== undefined) entries.push(`gap:${style.gap}px`);

  if (layout === 'FlexRow') {
    entries.push('display:flex');
    entries.push('flex-direction:row');
  }
  if (layout === 'FlexColumn') {
    entries.push('display:flex');
    entries.push('flex-direction:column');
  }
  if (layout === 'Grid') {
    entries.push('display:grid');
  }

  if (style.padding) {
    const padding = spacingToCss(style.padding);
    if (padding) entries.push(`padding:${padding}`);
  }
  if (style.margin) {
    const margin = spacingToCss(style.margin);
    if (margin) entries.push(`margin:${margin}`);
  }

  if (style.anchor || style.top !== undefined || style.left !== undefined || style.right !== undefined || style.bottom !== undefined) {
    entries.push('position:absolute');
    if (style.top !== undefined) entries.push(`top:${style.top}px`);
    if (style.left !== undefined) entries.push(`left:${style.left}px`);
    if (style.right !== undefined) entries.push(`right:${style.right}px`);
    if (style.bottom !== undefined) entries.push(`bottom:${style.bottom}px`);
  }

  if (style.shadow) {
    entries.push(`box-shadow:${style.shadow.offsetX}px ${style.shadow.offsetY}px ${style.shadow.blur}px ${style.shadow.color}`);
  }

  if (style.blur) {
    entries.push(`backdrop-filter:blur(${style.blur}px)`);
  }

  return entries.join(';');
}

function parseStyleMap(styleText: string): StyleMap {
  const parts = styleText.split(';').map((s) => s.trim()).filter(Boolean);
  const map: StyleMap = {};

  parts.forEach((part) => {
    const [key, ...rest] = part.split(':');
    if (!key || rest.length === 0) return;
    map[key.trim()] = rest.join(':').trim();
  });

  return map;
}

function parseStyle(styleText: string): UIStyle {
  const style: UIStyle = {};
  const map = parseStyleMap(styleText);

  for (const [cssKey, value] of Object.entries(map)) {
    const mappedKey = STYLE_KEY_MAP[cssKey];
    if (!mappedKey) continue;

    switch (mappedKey) {
      case 'width':
      case 'height':
        style[mappedKey] = value.endsWith('px') ? parseFloat(value) : value;
        break;
      case 'borderRadius':
      case 'borderWidth':
      case 'fontSize':
      case 'gap':
        style[mappedKey] = parseFloat(value);
        break;
      case 'opacity':
        style[mappedKey] = parseFloat(value);
        break;
      default:
        style[mappedKey] = value as never;
        break;
    }
  }

  if (map.padding) {
    const spacing = cssToSpacing(map.padding);
    if (spacing) style.padding = spacing;
  }
  if (map.margin) {
    const spacing = cssToSpacing(map.margin);
    if (spacing) style.margin = spacing;
  }

  if (map.position === 'absolute') {
    style.anchor = { horizontal: 'left', vertical: 'top' };
    if (map.top) style.top = parseFloat(map.top);
    if (map.left) style.left = parseFloat(map.left);
    if (map.right) style.right = parseFloat(map.right);
    if (map.bottom) style.bottom = parseFloat(map.bottom);
  }

  return style;
}

function createId(): string {
  return (globalThis.crypto && 'randomUUID' in globalThis.crypto)
    ? globalThis.crypto.randomUUID()
    : `ui_${Math.random().toString(36).slice(2, 10)}`;
}

function elementTypeFromTag(tag: string, element: Element): UIElement['type'] {
  const dataType = element.getAttribute('data-arsist-type');
  if (dataType) return dataType as UIElement['type'];

  switch (tag) {
    case 'button':
      return 'Button';
    case 'span':
    case 'p':
    case 'h1':
    case 'h2':
    case 'h3':
      return 'Text';
    case 'img':
      return 'Image';
    case 'input':
      return element.getAttribute('type') === 'range' ? 'Slider' : 'Input';
    default:
      return 'Panel';
  }
}

function elementToHtml(element: UIElement, isRoot = false): string {
  const tag = ELEMENT_TAG_MAP[element.type] || 'div';
  const styleText = serializeStyle(element.style || {}, element.layout);
  const attrs: string[] = [];
  attrs.push(`data-arsist-id="${element.id}"`);
  attrs.push(`data-arsist-type="${element.type}"`);
  if (element.layout) attrs.push(`data-arsist-layout="${element.layout}"`);
  if (isRoot) attrs.push('data-arsist-root="true"');
  if (styleText) attrs.push(`style="${styleText}"`);

  if (element.type === 'Image') {
    const src = element.content || '';
    return `<img ${attrs.join(' ')} src="${src}" />`;
  }

  if (element.type === 'Input') {
    const placeholder = element.content || '';
    return `<input ${attrs.join(' ')} placeholder="${placeholder}" />`;
  }

  if (element.type === 'Slider') {
    return `<input ${attrs.join(' ')} type="range" />`;
  }

  const childrenHtml = (element.children || []).map((child) => elementToHtml(child)).join('');
  const content = element.content || '';
  return `<${tag} ${attrs.join(' ')}>${content}${childrenHtml}</${tag}>`;
}

function detectLayoutFromStyle(map: StyleMap): UIElement['layout'] | undefined {
  if (map.display === 'grid') return 'Grid';
  if (map.display === 'flex') {
    return map['flex-direction'] === 'row' ? 'FlexRow' : 'FlexColumn';
  }
  if (map.position === 'absolute') return 'Absolute';
  return undefined;
}

function parseElement(element: Element): UIElement {
  const tagName = element.tagName.toLowerCase();
  const uiType = elementTypeFromTag(tagName, element);
  const id = element.getAttribute('data-arsist-id') || createId();
  const styleAttr = element.getAttribute('style') || '';
  const styleMap = parseStyleMap(styleAttr);
  const layout = element.getAttribute('data-arsist-layout') || detectLayoutFromStyle(styleMap);
  const style = parseStyle(styleAttr);

  const children = Array.from(element.children).map((child) => parseElement(child));

  const content = uiType === 'Text' || uiType === 'Button'
    ? element.textContent || ''
    : uiType === 'Image'
      ? element.getAttribute('src') || ''
      : uiType === 'Input'
        ? element.getAttribute('placeholder') || ''
        : undefined;

  return {
    id,
    type: uiType,
    layout: layout as UIElement['layout'],
    style,
    content,
    children,
  };
}

export function layoutToHtml(layout: UILayoutData): string {
  return elementToHtml(layout.root, true);
}

export function htmlToLayout(html: string, existingLayout?: UILayoutData): UILayoutData {
  const parser = new DOMParser();
  const doc = parser.parseFromString(html, 'text/html');
  const rootElement = doc.querySelector('[data-arsist-root="true"]') || doc.body.firstElementChild;

  const root = rootElement ? parseElement(rootElement) : {
    id: existingLayout?.root.id || createId(),
    type: 'Panel',
    layout: 'FlexColumn',
    style: {},
    children: [],
  };

  return {
    id: existingLayout?.id || createId(),
    name: existingLayout?.name || 'MainUI',
    root,
  };
}