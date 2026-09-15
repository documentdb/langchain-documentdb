package io.documentdb.langchain4j;

/** Vector index algorithms supported by DocumentDB. */
public enum DocumentDBVectorIndexKind {
  IVF("vector-ivf"),
  HNSW("vector-hnsw"),
  DISKANN("vector-diskann");

  private final String wireName;

  DocumentDBVectorIndexKind(String wireName) {
    this.wireName = wireName;
  }

  String wireName() {
    return wireName;
  }
}
