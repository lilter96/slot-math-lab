import sprites from './sprites.json';
import { useId } from 'react';
const symbols = ['s_bonus_loop_000', 's_doho_wild_000', 's_pic1_rottweiler_dog_000', 's_Pic2_Shih_tzu_dog_000',
  's_pic3_Pug_Dog_000', 's_pic4_Dachshund_dog_000', 's_pic5_dog_collar_000', 's_pic6_dog_bone_000',
  's_royals_a_000', 's_royals_k_000', 's_royals_q_000', 's_royals_j_000', 's_royals_10_000'];
export function SymbolSprite({ symbol }: { symbol: number }) { return <Sprite name={symbols[symbol - 1]} />; }
export default function Sprite({ name, className }: { name: string; className?: string }) {
  const clip = useId();
  const sprite = sprites[name as keyof typeof sprites];
  if (!sprite) return null;
  return <svg className={className} viewBox={`${sprite.x} ${sprite.y} ${sprite.width} ${sprite.height}`}
    aria-hidden="true" focusable="false" preserveAspectRatio="xMidYMid meet">
    <defs><clipPath id={clip}><rect x={sprite.x} y={sprite.y} width={sprite.width} height={sprite.height} /></clipPath></defs>
    <image href={sprite.url} width={sprite.atlasWidth} height={sprite.atlasHeight} clipPath={`url(#${clip})`} />
  </svg>;
}
