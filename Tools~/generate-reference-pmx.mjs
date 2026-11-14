// SPDX-License-Identifier: Apache-2.0
// Copyright 2026 VeraFirefly contributors

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createHash } from "node:crypto";

class Reader {
  constructor(buffer) { this.buffer = buffer; this.offset = 0; }
  take(size) {
    if (!Number.isInteger(size) || size < 0 || this.offset + size > this.buffer.length)
      throw new Error("Invalid PMX length or unexpected EOF");
    const value = this.buffer.subarray(this.offset, this.offset + size);
    this.offset += size;
    return value;
  }
  u8() { return this.take(1).readUInt8(); }
  u16() { return this.take(2).readUInt16LE(); }
  i32() { return this.take(4).readInt32LE(); }
  f32() { const value = this.take(4).readFloatLE(); if (!Number.isFinite(value)) throw new Error("Non-finite PMX value"); return value; }
  vec(size = 3) { return Array.from({ length: size }, () => this.f32()); }
  count() { const value = this.i32(); if (value < 0 || value > 100000000) throw new Error("Invalid PMX count"); return value; }
  string() { return this.take(this.count()).toString(this.encoding); }
  index(size, unsigned = false) {
    const value = this.take(size);
    if (size === 1) return unsigned ? value.readUInt8() : value.readInt8();
    if (size === 2) return unsigned ? value.readUInt16LE() : value.readInt16LE();
    if (size === 4) return unsigned ? value.readUInt32LE() : value.readInt32LE();
    throw new Error(`Invalid PMX index size: ${size}`);
  }
}

function readPmx(buffer) {
  const r = new Reader(buffer);
  if (r.take(4).toString("ascii") !== "PMX ") throw new Error("Invalid PMX signature");
  const version = r.f32();
  if (Math.abs(version - 2) > 0.001 && Math.abs(version - 2.1) > 0.001) throw new Error("Unsupported PMX version");
  const globals = [...r.take(r.u8())];
  if (globals.length < 8 || globals[0] > 1) throw new Error("Invalid PMX header");
  const [encoding, extraUv, vertexSize, textureSize, materialSize, boneSize, morphSize, rigidSize] = globals;
  for (const size of globals.slice(2, 8)) if (![1, 2, 4].includes(size)) throw new Error("Invalid PMX index size");
  r.encoding = encoding === 0 ? "utf16le" : "utf8";
  const names = [r.string(), r.string()];
  const comments = [r.string(), r.string()];
  const vertexCount = r.count();
  for (let i = 0; i < vertexCount; i++) {
    r.take(32 + extraUv * 16);
    switch (r.u8()) {
      case 0: r.take(boneSize); break;
      case 1: r.take(2 * boneSize + 4); break;
      case 2: case 4: r.take(4 * boneSize + 16); break;
      case 3: r.take(2 * boneSize + 40); break;
      default: throw new Error("Unknown PMX vertex weight type");
    }
    r.f32();
  }
  const surfaceIndexCount = r.count();
  r.take(surfaceIndexCount * vertexSize);
  const textureCount = r.count();
  for (let i = 0; i < textureCount; i++) r.string();
  const materialCount = r.count();
  for (let i = 0; i < materialCount; i++) {
    r.string(); r.string(); r.take(66 + 2 * textureSize);
    const sharedToon = r.u8();
    if (sharedToon > 1) throw new Error("Invalid PMX shared toon flag");
    r.take(sharedToon ? 1 : textureSize);
    r.string(); r.i32();
  }
  const bones = [];
  const boneCount = r.count();
  for (let i = 0; i < boneCount; i++) {
    const bone = {
      name: r.string(), english: r.string(), position: r.vec(),
      parent: r.index(boneSize), layer: r.i32(), flags: r.u16(),
    };
    bone.tail = bone.flags & 1 ? r.index(boneSize) : r.vec();
    if (bone.flags & 0x300) bone.inherit = { parent: r.index(boneSize), weight: r.f32() };
    if (bone.flags & 0x400) bone.fixedAxis = r.vec();
    if (bone.flags & 0x800) bone.localAxes = [r.vec(), r.vec()];
    if (bone.flags & 0x2000) bone.externalParent = r.i32();
    if (bone.flags & 0x20) {
      bone.ik = { target: r.index(boneSize), iterations: r.i32(), angle: r.f32(), links: [] };
      const count = r.count();
      for (let j = 0; j < count; j++) {
        const link = { bone: r.index(boneSize) };
        if (r.u8()) { link.min = r.vec(); link.max = r.vec(); }
        bone.ik.links.push(link);
      }
    }
    bones.push(bone);
  }
  const morphs = [];
  const morphCount = r.count();
  for (let i = 0; i < morphCount; i++) {
    const morph = { name: r.string(), english: r.string(), panel: r.u8(), type: r.u8(), offsets: [] };
    const count = r.count();
    for (let j = 0; j < count; j++) {
      switch (morph.type) {
        case 0: case 9: morph.offsets.push({ morph: r.index(morphSize), weight: r.f32() }); break;
        case 1: r.take(vertexSize + 12); break;
        case 2: morph.offsets.push({ bone: r.index(boneSize), translation: r.vec(), rotation: r.vec(4) }); break;
        case 3: case 4: case 5: case 6: case 7: r.take(vertexSize + 16); break;
        case 8: r.take(materialSize + 1 + 28 * 4); break;
        case 10: r.take(rigidSize + 1 + 6 * 4); break;
        default: throw new Error(`Unknown PMX morph type: ${morph.type}`);
      }
    }
    // Keep skeletal/group effects; mesh, material and physics morphs become name placeholders.
    if (![0, 2, 9].includes(morph.type)) morph.type = 1;
    morphs.push(morph);
  }
  const frameCount = r.count();
  for (let i = 0; i < frameCount; i++) {
    r.string(); r.string(); r.u8();
    const count = r.count();
    for (let j = 0; j < count; j++) {
      const type = r.u8();
      if (type > 1) throw new Error("Invalid display element");
      r.index(type ? morphSize : boneSize);
    }
  }
  const rigidCount = r.count();
  // Source physics is not used. For generated files, require a complete, physics-free payload.
  let jointCount;
  if (rigidCount === 0) jointCount = r.count();
  return { names, comments, bones, morphs, vertexCount, surfaceIndexCount, textureCount, materialCount, rigidCount, jointCount, offset: r.offset };
}

class Writer {
  constructor() { this.parts = []; }
  bytes(value) { this.parts.push(Buffer.from(value)); }
  u8(value) { const b = Buffer.alloc(1); b.writeUInt8(value); this.parts.push(b); }
  u16(value) { const b = Buffer.alloc(2); b.writeUInt16LE(value); this.parts.push(b); }
  i16(value) { const b = Buffer.alloc(2); b.writeInt16LE(value); this.parts.push(b); }
  i32(value) { const b = Buffer.alloc(4); b.writeInt32LE(value); this.parts.push(b); }
  f32(value) { const b = Buffer.alloc(4); b.writeFloatLE(value); this.parts.push(b); }
  string(value) { const b = Buffer.from(value, "utf8"); this.i32(b.length); this.bytes(b); }
  vec(value) { for (const component of value) this.f32(component); }
  result() { return Buffer.concat(this.parts); }
}

function validateRig({ bones, morphs }) {
  if (!bones.length || bones.length > 32767 || morphs.length > 32767) throw new Error("Unsupported rig size");
  const check = (index, count, nullable = false) => {
    if (!Number.isInteger(index) || index < (nullable ? -1 : 0) || index >= count) throw new Error(`Invalid rig index: ${index}`);
  };
  for (let i = 0; i < bones.length; i++) {
    const bone = bones[i];
    check(bone.parent, bones.length, true);
    if (bone.flags & 1) check(bone.tail, bones.length, true);
    if (bone.inherit) check(bone.inherit.parent, bones.length);
    if (bone.ik) {
      check(bone.ik.target, bones.length);
      for (const link of bone.ik.links) check(link.bone, bones.length);
    }
    const visited = new Set([i]);
    for (let parent = bone.parent; parent !== -1; parent = bones[parent].parent) {
      check(parent, bones.length);
      if (visited.has(parent)) throw new Error(`Cyclic bone hierarchy: ${bone.name}`);
      visited.add(parent);
    }
  }
  for (const morph of morphs) for (const offset of morph.offsets) {
    if (morph.type === 2) check(offset.bone, bones.length);
    else check(offset.morph, morphs.length);
  }
  for (const name of ["センター", "上半身", "下半身", "左腕", "右腕", "左ひじ", "右ひじ", "左手首", "右手首"])
    if (!bones.some(bone => bone.name === name)) throw new Error(`Missing humanoid bone: ${name}`);
}

function generatePmx(source) {
  const w = new Writer();
  w.bytes(Buffer.from("PMX ", "ascii")); w.f32(2.0); w.u8(8);
  w.bytes([1, 0, 1, 1, 1, 2, 2, 1]);
  w.string("YYB式初音ミク 骨格参照リグ"); w.string("YYB Miku Skeleton Reference Rig");
  const comment = `Skeleton extracted from ${source.names[0]}. Model: SANMUYYB. No character mesh, textures or physics. Source model terms apply to derived rig data.`;
  w.string(comment); w.string(comment);
  const root = source.bones.findIndex(bone => bone.parent === -1);
  if (root < 0) throw new Error("Skeleton has no root");
  w.i32(3);
  for (const position of [[0, 0, 0], [0.001, 0, 0], [0, 0.001, 0]]) {
    w.vec(position); w.vec([0, 0, -1]); w.vec([0, 0]); w.u8(0); w.i16(root); w.f32(0);
  }
  w.i32(3); w.bytes([0, 1, 2]); w.i32(0); w.i32(1);
  w.string("互換用透明材質"); w.string("Transparent compatibility material");
  w.vec([0, 0, 0, 0]); w.vec([0, 0, 0]); w.f32(0); w.vec([0, 0, 0]);
  w.u8(0); w.vec([0, 0, 0, 0]); w.f32(0); w.u8(255); w.u8(255); w.u8(0); w.u8(1); w.u8(0);
  w.string("Compatibility triangle for PMX importers that require vertices."); w.i32(3);
  w.i32(source.bones.length);
  for (const bone of source.bones) {
    w.string(bone.name); w.string(bone.english); w.vec(bone.position); w.i16(bone.parent); w.i32(bone.layer); w.u16(bone.flags);
    if (bone.flags & 1) w.i16(bone.tail); else w.vec(bone.tail);
    if (bone.inherit) { w.i16(bone.inherit.parent); w.f32(bone.inherit.weight); }
    if (bone.fixedAxis) w.vec(bone.fixedAxis);
    if (bone.localAxes) for (const axis of bone.localAxes) w.vec(axis);
    if (bone.externalParent !== undefined) w.i32(bone.externalParent);
    if (bone.ik) {
      w.i16(bone.ik.target); w.i32(bone.ik.iterations); w.f32(bone.ik.angle); w.i32(bone.ik.links.length);
      for (const link of bone.ik.links) {
        w.i16(link.bone); w.u8(link.min ? 1 : 0);
        if (link.min) { w.vec(link.min); w.vec(link.max); }
      }
    }
  }
  w.i32(source.morphs.length);
  for (const morph of source.morphs) {
    w.string(morph.name); w.string(morph.english); w.u8(morph.panel); w.u8(morph.type); w.i32(morph.offsets.length);
    for (const offset of morph.offsets) {
      if (morph.type === 2) { w.i16(offset.bone); w.vec(offset.translation); w.vec(offset.rotation); }
      else { w.i16(offset.morph); w.f32(offset.weight); }
    }
  }
  w.i32(2);
  w.string("Root"); w.string("Root"); w.u8(1); w.i32(1); w.u8(0); w.i16(root);
  w.string("表情"); w.string("Facial"); w.u8(1); w.i32(source.morphs.length);
  for (let i = 0; i < source.morphs.length; i++) { w.u8(1); w.i16(i); }
  w.i32(0); w.i32(0);
  return w.result();
}

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const sourceArgument = process.argv[2];
if (!sourceArgument) throw new Error('Usage: node Tools~/generate-reference-pmx.mjs "YYB Hatsune Miku_10th_v1.02.pmx" [output.pmx]');
const sourcePath = path.resolve(sourceArgument);
const outputPath = path.resolve(process.argv[3] ?? path.join(scriptDirectory, "../Editor/Model/_model.pmx"));
if (sourcePath.toLowerCase() === outputPath.toLowerCase()) throw new Error("Output must not overwrite the source model");
const sourceData = fs.readFileSync(sourcePath);
const source = readPmx(sourceData);
if (!/YYB/i.test(source.names.join(" "))) throw new Error("Expected a YYB model");
validateRig(source);
const data = generatePmx(source);
const generated = readPmx(data);
validateRig(generated);
if (JSON.stringify(generated.bones) !== JSON.stringify(source.bones) || JSON.stringify(generated.morphs) !== JSON.stringify(source.morphs))
  throw new Error("Generated skeleton or morphs differ from the source");
if (generated.vertexCount !== 3 || generated.surfaceIndexCount !== 3 || generated.materialCount !== 1 || generated.textureCount !== 0 || generated.rigidCount !== 0 || generated.jointCount !== 0 || generated.offset !== data.length)
  throw new Error("Invalid skeleton-only PMX payload");
fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(`${outputPath}.tmp`, data);
fs.renameSync(`${outputPath}.tmp`, outputPath);
console.log(JSON.stringify({ outputPath, sourceModel: source.names[0], sourceSha256: createHash("sha256").update(sourceData).digest("hex"), bytes: data.length, boneCount: generated.bones.length, ikCount: generated.bones.filter(bone => bone.ik).length, morphCount: generated.morphs.length }, null, 2));
