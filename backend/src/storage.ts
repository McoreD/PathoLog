import { mkdir, writeFile, readFile } from "fs/promises";
import path from "path";
import { S3Client, PutObjectCommand, GetObjectCommand } from "@aws-sdk/client-s3";
import { getSignedUrl } from "@aws-sdk/s3-request-presigner";
import { env } from "./env.js";
import { logger } from "./logger.js";

export type StoredFile = {
  storageProvider: "local" | "s3";
  storageBucket?: string;
  storageKey: string;
};

async function ensureDir(dir: string) {
  await mkdir(dir, { recursive: true });
}

class LocalStorage {
  constructor(private basePath: string) {}

  async saveFile(key: string, buffer: Buffer, _contentType?: string): Promise<StoredFile> {
    const fullPath = path.join(this.basePath, key);
    await ensureDir(path.dirname(fullPath));
    await writeFile(fullPath, buffer);
    return { storageProvider: "local", storageKey: key };
  }

  async getFileBuffer(key: string): Promise<Buffer> {
    const fullPath = path.join(this.basePath, key);
    return readFile(fullPath);
  }
}

class S3Storage {
  private client: S3Client;
  constructor(private bucket: string) {
    this.client = new S3Client({
      region: env.S3_REGION,
      endpoint: env.S3_ENDPOINT || undefined,
      forcePathStyle: Boolean(env.S3_ENDPOINT),
      credentials: env.S3_ACCESS_KEY_ID
        ? {
            accessKeyId: env.S3_ACCESS_KEY_ID,
            secretAccessKey: env.S3_SECRET_ACCESS_KEY || "",
          }
        : undefined,
    });
  }

  async saveFile(key: string, buffer: Buffer, contentType?: string): Promise<StoredFile> {
    await this.client.send(
      new PutObjectCommand({
        Bucket: this.bucket,
        Key: key,
        Body: buffer,
        ContentType: contentType,
      }),
    );
    return { storageProvider: "s3", storageBucket: this.bucket, storageKey: key };
  }

  async getFileBuffer(key: string): Promise<Buffer> {
    const { Body } = await this.client.send(
      new GetObjectCommand({
        Bucket: this.bucket,
        Key: key,
      }),
    );
    if (!Body) throw new Error("Empty S3 body");
    const chunks: Buffer[] = [];
    for await (const chunk of Body as any as AsyncIterable<Buffer>) {
      chunks.push(Buffer.from(chunk));
    }
    return Buffer.concat(chunks);
  }
}

export type SignedUrl = { url: string; expiresAt: number };

export function isSignedUrlSupported() {
  return env.STORAGE_PROVIDER === "s3";
}

export async function createSignedUrl(key: string, expiresInSeconds: number): Promise<SignedUrl | null> {
  if (env.STORAGE_PROVIDER !== "s3" || !env.S3_BUCKET) return null;
  const client = new S3Client({
    region: env.S3_REGION,
    endpoint: env.S3_ENDPOINT || undefined,
    forcePathStyle: Boolean(env.S3_ENDPOINT),
    credentials: env.S3_ACCESS_KEY_ID
      ? {
          accessKeyId: env.S3_ACCESS_KEY_ID,
          secretAccessKey: env.S3_SECRET_ACCESS_KEY || "",
        }
      : undefined,
  });
  const command = new GetObjectCommand({
    Bucket: env.S3_BUCKET,
    Key: key,
  });
  const url = await getSignedUrl(client, command, { expiresIn: expiresInSeconds });
  return { url, expiresAt: Math.floor(Date.now() / 1000) + expiresInSeconds };
}

export function createStorage() {
  if (env.STORAGE_PROVIDER === "s3") {
    if (!env.S3_BUCKET) {
      throw new Error("S3_BUCKET is required for s3 storage provider");
    }
    logger.info("Using S3 storage provider");
    return new S3Storage(env.S3_BUCKET);
  }
  logger.info({ path: env.LOCAL_STORAGE_PATH }, "Using local storage provider");
  return new LocalStorage(env.LOCAL_STORAGE_PATH);
}
