package io.documentdb.langchain4j;

import dev.langchain4j.store.embedding.filter.Filter;
import dev.langchain4j.store.embedding.filter.comparison.ContainsString;
import dev.langchain4j.store.embedding.filter.comparison.IsEqualTo;
import dev.langchain4j.store.embedding.filter.comparison.IsGreaterThan;
import dev.langchain4j.store.embedding.filter.comparison.IsGreaterThanOrEqualTo;
import dev.langchain4j.store.embedding.filter.comparison.IsIn;
import dev.langchain4j.store.embedding.filter.comparison.IsLessThan;
import dev.langchain4j.store.embedding.filter.comparison.IsLessThanOrEqualTo;
import dev.langchain4j.store.embedding.filter.comparison.IsNotEqualTo;
import dev.langchain4j.store.embedding.filter.comparison.IsNotIn;
import dev.langchain4j.store.embedding.filter.logical.And;
import dev.langchain4j.store.embedding.filter.logical.Not;
import dev.langchain4j.store.embedding.filter.logical.Or;
import java.util.regex.Pattern;
import org.bson.Document;

final class DocumentDBFilterMapper {
  private DocumentDBFilterMapper() {}

  static Document toBson(Filter filter, String metadataKey) {
    if (filter instanceof IsEqualTo value) {
      return new Document(field(metadataKey, value.key()), value.comparisonValue());
    }
    if (filter instanceof IsNotEqualTo value) {
      return comparison(metadataKey, value.key(), "$ne", value.comparisonValue());
    }
    if (filter instanceof IsGreaterThan value) {
      return comparison(metadataKey, value.key(), "$gt", value.comparisonValue());
    }
    if (filter instanceof IsGreaterThanOrEqualTo value) {
      return comparison(metadataKey, value.key(), "$gte", value.comparisonValue());
    }
    if (filter instanceof IsLessThan value) {
      return comparison(metadataKey, value.key(), "$lt", value.comparisonValue());
    }
    if (filter instanceof IsLessThanOrEqualTo value) {
      return comparison(metadataKey, value.key(), "$lte", value.comparisonValue());
    }
    if (filter instanceof IsIn value) {
      return comparison(metadataKey, value.key(), "$in", value.comparisonValues());
    }
    if (filter instanceof IsNotIn value) {
      return comparison(metadataKey, value.key(), "$nin", value.comparisonValues());
    }
    if (filter instanceof ContainsString value) {
      Pattern pattern = Pattern.compile(Pattern.quote(value.comparisonValue()));
      return new Document(field(metadataKey, value.key()), pattern);
    }
    if (filter instanceof And value) {
      return new Document(
          "$and",
          java.util.List.of(toBson(value.left(), metadataKey), toBson(value.right(), metadataKey)));
    }
    if (filter instanceof Or value) {
      return new Document(
          "$or",
          java.util.List.of(toBson(value.left(), metadataKey), toBson(value.right(), metadataKey)));
    }
    if (filter instanceof Not value) {
      return new Document("$nor", java.util.List.of(toBson(value.expression(), metadataKey)));
    }
    throw new UnsupportedOperationException(
        "Unsupported LangChain4j filter: " + filter.getClass().getName());
  }

  private static Document comparison(
      String metadataKey, String key, String operator, Object value) {
    return new Document(field(metadataKey, key), new Document(operator, value));
  }

  private static String field(String metadataKey, String key) {
    if (key == null || key.isBlank() || key.startsWith("$") || key.indexOf('\0') >= 0) {
      throw new IllegalArgumentException("Metadata filter key must be a non-empty safe name");
    }
    return metadataKey + "." + key;
  }
}
